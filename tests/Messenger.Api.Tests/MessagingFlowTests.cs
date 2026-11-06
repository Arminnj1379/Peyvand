using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Messenger.Api.Tests;

[Collection("auth")]
public sealed class MessagingFlowTests(MessengerFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private async Task<HttpClient> CleanClientAsync()
    {
        await factory.CleanAsync();
        return factory.CreateClient();
    }

    private static async Task<AuthResponse> RegisterAsync(HttpClient client, string username)
    {
        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username,
            displayName = username,
            password = "password123",
            phone = (string?)null,
            deviceLabel = "test"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    private static void AuthAs(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private static async Task<JsonElement> PostAsync(HttpClient client, string url, object body, HttpStatusCode expect)
    {
        var res = await client.PostAsJsonAsync(url, body);
        Assert.Equal(expect, res.StatusCode);
        var doc = await res.Content.ReadFromJsonAsync<JsonDocument>(Json);
        return doc!.RootElement;
    }

    [Fact]
    public async Task Dm_create_send_history_edit_delete_flow()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "msg.a");
        var bClient = factory.CreateClient();
        var b = await RegisterAsync(bClient, "msg.b");

        AuthAs(client, a.AccessToken);
        var dm = await PostAsync(client, "/api/v1/chats/dm",
            new { otherUserId = b.User.Id }, HttpStatusCode.OK);
        var chatId = dm.GetProperty("id").GetString()!;
        Assert.Equal("Dm", dm.GetProperty("type").GetString());

        // Same pair returns the existing DM (idempotent create).
        var dm2 = await PostAsync(client, "/api/v1/chats/dm",
            new { otherUserId = b.User.Id }, HttpStatusCode.OK);
        Assert.Equal(chatId, dm2.GetProperty("id").GetString());

        // Send + reply.
        var m1 = await PostAsync(client, $"/api/v1/chats/{chatId}/messages",
            new { body = "hello", clientMessageId = "k-1", replyToMessageId = (string?)null },
            HttpStatusCode.Created);
        Assert.Equal(1, m1.GetProperty("seq").GetInt64());
        var m1Id = m1.GetProperty("id").GetString()!;

        var m2 = await PostAsync(client, $"/api/v1/chats/{chatId}/messages",
            new { body = "hi back", clientMessageId = "k-2", replyToMessageId = m1Id },
            HttpStatusCode.Created);
        Assert.Equal("Reply", m2.GetProperty("kind").GetString());
        Assert.Equal(m1Id, m2.GetProperty("replyTo").GetProperty("messageId").GetString());

        // History page (cursor).
        var hist = await client.GetAsync($"/api/v1/chats/{chatId}/messages?limit=10");
        Assert.Equal(HttpStatusCode.OK, hist.StatusCode);
        var page = (await hist.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(2, page.RootElement.GetProperty("items").GetArrayLength());
        Assert.False(page.RootElement.GetProperty("hasMore").GetBoolean());

        // Edit + delete.
        var edit = await client.PatchAsJsonAsync($"/api/v1/messages/{m1Id}", new { body = "hello edited" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var edited = (await edit.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.True(edited.RootElement.GetProperty("isEdited").GetBoolean());

        var del = await client.DeleteAsync($"/api/v1/messages/{m1Id}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);

        // Chat list shows the conversation with preview.
        var list = await client.GetAsync("/api/v1/chats?limit=20");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var chats = (await list.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(1, chats.RootElement.GetArrayLength());

        // Mark read.
        var read = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/read", new { seq = 2 });
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
    }

    [Fact]
    public async Task MarkRead_recomputes_unread_server_side()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "rd.a");
        var bClient = factory.CreateClient();
        var b = await RegisterAsync(bClient, "rd.b");

        AuthAs(client, a.AccessToken);
        var dm = await PostAsync(client, "/api/v1/chats/dm",
            new { otherUserId = b.User.Id }, HttpStatusCode.OK);
        var chatId = dm.GetProperty("id").GetString()!;

        for (var i = 1; i <= 3; i++)
        {
            await PostAsync(client, $"/api/v1/chats/{chatId}/messages",
                new { body = $"m{i}", clientMessageId = $"rk-{i}", replyToMessageId = (string?)null },
                HttpStatusCode.Created);
        }

        AuthAs(bClient, b.AccessToken);
        async Task<int> UnreadAsync()
        {
            var list = await bClient.GetAsync("/api/v1/chats?limit=20");
            var chats = (await list.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
            return chats.RootElement[0].GetProperty("unreadCount").GetInt32();
        }

        Assert.Equal(3, await UnreadAsync());

        // Partial read recomputes from the live counter (3 - 2 = 1).
        Assert.Equal(HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/read", new { seq = 2 })).StatusCode);
        Assert.Equal(1, await UnreadAsync());

        // Stale watermark is a no-op (monotonic, never regresses).
        Assert.Equal(HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/read", new { seq = 1 })).StatusCode);
        Assert.Equal(1, await UnreadAsync());

        Assert.Equal(HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/read", new { seq = 3 })).StatusCode);
        Assert.Equal(0, await UnreadAsync());
    }

    [Fact]
    public async Task Concurrent_dm_create_collapses_to_one()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "cc.a");
        var b = await RegisterAsync(factory.CreateClient(), "cc.b");

        AuthAs(client, a.AccessToken);
        // Parallel creates race the pair-key unique index; every response —
        // whether it won the insert or resolved the loser path — returns the
        // same conversation. No duplicate DMs, no 500s.
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id })));
        var ids = new List<string>();
        foreach (var res in responses)
        {
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            ids.Add((await res.Content.ReadFromJsonAsync<JsonDocument>(Json))!
                .RootElement.GetProperty("id").GetString()!);
        }

        Assert.Single(ids.Distinct());
    }

    [Fact]
    public async Task Retry_with_same_client_message_id_returns_original()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "idem.a");
        var b = await RegisterAsync(factory.CreateClient(), "idem.b");

        AuthAs(client, a.AccessToken);
        var dm = await PostAsync(client, "/api/v1/chats/dm",
            new { otherUserId = b.User.Id }, HttpStatusCode.OK);
        var chatId = dm.GetProperty("id").GetString()!;

        var body = new { body = "once", clientMessageId = "retry-key", replyToMessageId = (string?)null };
        var first = await PostAsync(client, $"/api/v1/chats/{chatId}/messages", body, HttpStatusCode.Created);
        var second = await PostAsync(client, $"/api/v1/chats/{chatId}/messages", body, HttpStatusCode.Created);

        Assert.Equal(first.GetProperty("id").GetString(), second.GetProperty("id").GetString());
        Assert.Equal(first.GetProperty("seq").GetInt64(), second.GetProperty("seq").GetInt64());

        var hist = await client.GetAsync($"/api/v1/chats/{chatId}/messages?limit=10");
        var page = (await hist.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(1, page.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Outsider_gets_forbidden_on_private_chat()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "priv.a");
        var b = await RegisterAsync(factory.CreateClient(), "priv.b");
        var c = await RegisterAsync(factory.CreateClient(), "priv.c");

        AuthAs(client, a.AccessToken);
        var dm = await PostAsync(client, "/api/v1/chats/dm",
            new { otherUserId = b.User.Id }, HttpStatusCode.OK);
        var chatId = dm.GetProperty("id").GetString()!;

        var cClient = factory.CreateClient();
        AuthAs(cClient, c.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await cClient.GetAsync($"/api/v1/chats/{chatId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
                new { body = "intrude", clientMessageId = "k-x", replyToMessageId = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cClient.GetAsync($"/api/v1/chats/{chatId}/messages?limit=10")).StatusCode);
    }

    [Fact]
    public async Task Group_pagination_pins_forward_and_members()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "grp.a");
        var bAuth = await RegisterAsync(factory.CreateClient(), "grp.b");

        AuthAs(client, a.AccessToken);
        var grp = await PostAsync(client, "/api/v1/chats/groups",
            new { title = "Team", memberIds = new[] { bAuth.User.Id }, description = (string?)null },
            HttpStatusCode.Created);
        var chatId = grp.GetProperty("id").GetString()!;

        for (var i = 1; i <= 5; i++)
        {
            await PostAsync(client, $"/api/v1/chats/{chatId}/messages",
                new { body = $"msg {i}", clientMessageId = $"k-{i}", replyToMessageId = (string?)null },
                HttpStatusCode.Created);
        }

        // Page 1: newest two.
        var p1 = await client.GetAsync($"/api/v1/chats/{chatId}/messages?limit=2");
        var page1 = (await p1.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.True(page1.RootElement.GetProperty("hasMore").GetBoolean());
        var items1 = page1.RootElement.GetProperty("items");
        Assert.Equal(2, items1.GetArrayLength());
        Assert.Equal(4, items1[0].GetProperty("seq").GetInt64());
        Assert.Equal(5, items1[1].GetProperty("seq").GetInt64());
        var cursor = page1.RootElement.GetProperty("nextCursor").GetInt64();

        // Page 2 via cursor.
        var p2 = await client.GetAsync($"/api/v1/chats/{chatId}/messages?limit=2&beforeSeq={cursor}");
        var page2 = (await p2.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var items2 = page2.RootElement.GetProperty("items");
        Assert.Equal(2, items2.GetArrayLength());
        Assert.Equal(2, items2[0].GetProperty("seq").GetInt64());

        // Sync after reconnect.
        var sync = await client.GetAsync($"/api/v1/chats/{chatId}/messages/sync?afterSeq=3&limit=10");
        var synced = (await sync.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(2, synced.RootElement.GetArrayLength());

        // Pin first message, list pins.
        var firstId = items2[0].GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/pins", new { messageId = firstId })).StatusCode);
        var pins = await client.GetAsync($"/api/v1/chats/{chatId}/pins");
        var pinned = (await pins.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(1, pinned.RootElement.GetArrayLength());

        // Forward into a second group.
        var grp2 = await PostAsync(client, "/api/v1/chats/groups",
            new { title = "Other", memberIds = new[] { bAuth.User.Id }, description = (string?)null },
            HttpStatusCode.Created);
        var chat2 = grp2.GetProperty("id").GetString()!;
        var fwd = await PostAsync(client, "/api/v1/messages/forward",
            new { sourceChatId = chatId, sourceMessageId = firstId, targetChatId = chat2, clientMessageId = "k-fwd" },
            HttpStatusCode.Created);
        Assert.Equal("Forward", fwd.GetProperty("kind").GetString());

        // Non-admin member cannot add members.
        var bClient = factory.CreateClient();
        AuthAs(bClient, bAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/members",
                new { userId = bAuth.User.Id })).StatusCode);
    }

    [Fact]
    public async Task Channel_subscriber_reads_but_cannot_post()
    {
        var client = await CleanClientAsync();
        var owner = await RegisterAsync(client, "chn.owner");
        var sub = await RegisterAsync(factory.CreateClient(), "chn.sub");

        AuthAs(client, owner.AccessToken);
        var ch = await PostAsync(client, "/api/v1/chats/channels",
            new { title = "News", description = (string?)null, subscriberIds = new[] { sub.User.Id } },
            HttpStatusCode.Created);
        var chatId = ch.GetProperty("id").GetString()!;

        await PostAsync(client, $"/api/v1/chats/{chatId}/messages",
            new { body = "broadcast", clientMessageId = "k-b", replyToMessageId = (string?)null },
            HttpStatusCode.Created);

        var sClient = factory.CreateClient();
        AuthAs(sClient, sub.AccessToken);
        Assert.Equal(HttpStatusCode.OK,
            (await sClient.GetAsync($"/api/v1/chats/{chatId}/messages?limit=10")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await sClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
                new { body = "nope", clientMessageId = "k-s", replyToMessageId = (string?)null })).StatusCode);
    }
}
