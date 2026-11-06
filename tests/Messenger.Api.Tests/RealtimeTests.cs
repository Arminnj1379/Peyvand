using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Messenger.Api.Tests;

/// <summary>
/// Real-time behavior over real SignalR connections (LongPolling transport —
/// the in-memory test server has no WebSockets): authentication, per-chat
/// authorization, live delivery to concurrent connections, typing, presence +
/// last-seen, delivered watermarks, and reconnect gap-fill via REST sync.
/// </summary>
[Collection("auth")]
public sealed class RealtimeTests(MessengerFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

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

    private HubConnection Connect(string token)
    {
        var url = new Uri(factory.Server.BaseAddress, "/hubs/messenger");
        return new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                o.AccessTokenProvider = () => Task.FromResult<string?>(token);
                // Route the client through the in-memory test server (no real sockets).
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                // TestServer has no WebSockets; LongPolling exercises the same hub pipeline.
                o.Transports = HttpTransportType.LongPolling;
            })
            .WithAutomaticReconnect()
            .Build();
    }

    private static async Task<T> ReceiveAsync<T>(TaskCompletionSource<T> tcs, string what)
    {
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(Wait));
        Assert.True(completed == tcs.Task, $"Timed out waiting for {what}.");
        return await tcs.Task;
    }

    private static async Task AssertNoReceiveAsync<T>(TaskCompletionSource<T> tcs, string what)
    {
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        Assert.True(completed != tcs.Task, $"Unexpected {what} received.");
    }

    [Fact]
    public async Task Unauthenticated_connection_is_rejected()
    {
        await CleanClientAsync();
        var url = new Uri(factory.Server.BaseAddress, "/hubs/messenger");
        await using var conn = new HubConnectionBuilder()
            .WithUrl(url, o =>
            {
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
            })
            .Build();

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => conn.StartAsync());
        Assert.Contains("401", ex.Message);
    }

    [Fact]
    public async Task Live_delivery_reaches_concurrent_connections_exactly_once()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "rt.a");
        var b = await RegisterAsync(factory.CreateClient(), "rt.b");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        await using var a1 = Connect(a.AccessToken);
        await using var a2 = Connect(a.AccessToken);
        await using var bConn = Connect(b.AccessToken);
        var gotA1 = new TaskCompletionSource<string>();
        var gotA2 = new TaskCompletionSource<string>();
        var gotB = new TaskCompletionSource<string>();
        var countA1 = 0;
        a1.On<JsonElement>("message:new", m =>
        {
            countA1++;
            gotA1.TrySetResult(m.GetProperty("id").GetString()!);
        });
        a2.On<JsonElement>("message:new", m => gotA2.TrySetResult(m.GetProperty("id").GetString()!));
        bConn.On<JsonElement>("message:new", m => gotB.TrySetResult(m.GetProperty("id").GetString()!));
        await a1.StartAsync();
        await a2.StartAsync();
        await bConn.StartAsync();

        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "live hello", clientMessageId = "rt-1", replyToMessageId = (string?)null });
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var sent = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var messageId = sent.RootElement.GetProperty("id").GetString()!;

        // Both of A's devices and B receive the same persisted message.
        Assert.Equal(messageId, await ReceiveAsync(gotA1, "message:new on A/1"));
        Assert.Equal(messageId, await ReceiveAsync(gotA2, "message:new on A/2"));
        Assert.Equal(messageId, await ReceiveAsync(gotB, "message:new on B"));

        // Persist-then-broadcast: exactly one delivery per connection (no dupes).
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(1, countA1);
    }

    [Fact]
    public async Task Typing_is_ephemeral_and_member_only()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "ty.a");
        var b = await RegisterAsync(factory.CreateClient(), "ty.b");
        var c = await RegisterAsync(factory.CreateClient(), "ty.c");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        await using var aConn = Connect(a.AccessToken);
        await using var bConn = Connect(b.AccessToken);
        await using var cConn = Connect(c.AccessToken);
        var typing = new TaskCompletionSource<string>();
        var leaked = new TaskCompletionSource<string>();
        bConn.On<JsonElement>("typing:update", e => typing.TrySetResult(e.GetProperty("userId").GetString()!));
        cConn.On<JsonElement>("typing:update", e => leaked.TrySetResult(e.GetProperty("userId").GetString()!));
        await aConn.StartAsync();
        await bConn.StartAsync();
        await cConn.StartAsync();

        await aConn.InvokeAsync("Typing", chatId, true);
        Assert.Equal(a.User.Id, await ReceiveAsync(typing, "typing:update for member"));

        // Outsider typing on the chat leaks nothing to anyone.
        await cConn.InvokeAsync("Typing", chatId, true);
        await AssertNoReceiveAsync(leaked, "typing:update for outsider");

        // Typing leaves no persistent trace: history is unaffected.
        var hist = await client.GetAsync($"/api/v1/chats/{chatId}/messages?limit=10");
        var page = (await hist.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(0, page.RootElement.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task Presence_online_offline_and_last_seen()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "pr.a");
        var b = await RegisterAsync(factory.CreateClient(), "pr.b");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });

        await using var bConn = Connect(b.AccessToken);
        var online = new TaskCompletionSource<string>();
        var offline = new TaskCompletionSource<JsonElement>();
        bConn.On<JsonElement>("presence:update", e =>
        {
            if (e.GetProperty("userId").GetString() != a.User.Id)
            {
                return;
            }

            if (e.GetProperty("isOnline").GetBoolean())
            {
                online.TrySetResult(a.User.Id);
            }
            else
            {
                offline.TrySetResult(e);
            }
        });
        await bConn.StartAsync();

        await using (var aConn = Connect(a.AccessToken))
        {
            await aConn.StartAsync();
            Assert.Equal(a.User.Id, await ReceiveAsync(online, "presence online"));
        }

        // Disconnect persists last-seen and broadcasts offline once.
        var off = await ReceiveAsync(offline, "presence offline");
        Assert.False(off.GetProperty("isOnline").GetBoolean());
        Assert.NotNull(off.GetProperty("lastSeenAt").GetString());

        var profile = await client.GetAsync($"/api/v1/users/{a.User.Id}");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        var user = (await profile.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.NotNull(user.RootElement.GetProperty("lastSeenAt").GetString());
    }

    [Fact]
    public async Task Delivered_watermark_advances_and_ignores_duplicates()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "dl.a");
        var b = await RegisterAsync(factory.CreateClient(), "dl.b");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        await using var aConn = Connect(a.AccessToken);
        await using var bConn = Connect(b.AccessToken);
        var delivered = new TaskCompletionSource<JsonElement>();
        var deliveries = 0;
        aConn.On<JsonElement>("message:delivered", e =>
        {
            deliveries++;
            delivered.TrySetResult(e);
        });
        await aConn.StartAsync();
        await bConn.StartAsync();

        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "ack me", clientMessageId = "dl-1", replyToMessageId = (string?)null });
        var sent = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var seq = sent.RootElement.GetProperty("seq").GetInt64();

        await bConn.InvokeAsync("Delivered", chatId, seq);
        var evt = await ReceiveAsync(delivered, "message:delivered");
        Assert.Equal(b.User.Id, evt.GetProperty("userId").GetString());
        Assert.Equal(seq, evt.GetProperty("seq").GetInt64());

        // Duplicate ack: stored watermark holds, no second event.
        await bConn.InvokeAsync("Delivered", chatId, seq);
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(1, deliveries);

        var chat = await client.GetAsync($"/api/v1/chats/{chatId}");
        var doc = (await chat.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var member = doc.RootElement.GetProperty("members").EnumerateArray()
            .First(m => m.GetProperty("userId").GetString() == b.User.Id);
        Assert.Equal(seq, member.GetProperty("lastDeliveredSeq").GetInt64());
    }

    [Fact]
    public async Task Outsider_subscribe_typing_and_ack_stay_silent()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "os.a");
        var b = await RegisterAsync(factory.CreateClient(), "os.b");
        var cAuth = await RegisterAsync(factory.CreateClient(), "os.c");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        await using var aConn = Connect(a.AccessToken);
        await using var cConn = Connect(cAuth.AccessToken);
        var leakedMsg = new TaskCompletionSource<string>();
        var leakedTyping = new TaskCompletionSource<string>();
        var leakedDelivered = new TaskCompletionSource<string>();
        cConn.On<JsonElement>("message:new", m => leakedMsg.TrySetResult(m.GetProperty("id").GetString()!));
        aConn.On<JsonElement>("typing:update", _ => leakedTyping.TrySetResult("x"));
        aConn.On<JsonElement>("message:delivered", _ => leakedDelivered.TrySetResult("x"));
        await aConn.StartAsync();
        await cConn.StartAsync();

        // Subscribe to a foreign chat silently skips; forged signals go nowhere.
        await cConn.InvokeAsync("Subscribe", new[] { chatId });
        await cConn.InvokeAsync("Typing", chatId, true);
        await cConn.InvokeAsync("Delivered", chatId, 1);

        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "private", clientMessageId = "os-1", replyToMessageId = (string?)null });

        await AssertNoReceiveAsync(leakedMsg, "message:new for outsider");
        await AssertNoReceiveAsync(leakedTyping, "typing from outsider");
        await AssertNoReceiveAsync(leakedDelivered, "delivered from outsider");
        _ = cAuth;
    }

    /// <summary>
    /// The reported live-chat bug: the recipient's connection predates the new
    /// chat, so it never joined the chat group. Creation must still reach them
    /// via their personal channel; after they subscribe (as the web client does
    /// on chat:updated), the first message arrives live — no refresh needed.
    /// Outsiders must stay silent (members-only recipients).
    /// </summary>
    [Fact]
    public async Task New_dm_notifies_other_member_live()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "nc.a");
        var b = await RegisterAsync(factory.CreateClient(), "nc.b");
        var cAuth = await RegisterAsync(factory.CreateClient(), "nc.c");

        await using var bConn = Connect(b.AccessToken);
        await using var cConn = Connect(cAuth.AccessToken);
        var created = new TaskCompletionSource<string>();
        var leaked = new TaskCompletionSource<string>();
        bConn.On<JsonElement>("chat:updated", e => created.TrySetResult(e.GetProperty("chatId").GetString()!));
        cConn.On<JsonElement>("chat:updated", e => leaked.TrySetResult(e.GetProperty("chatId").GetString()!));
        await bConn.StartAsync();
        await cConn.StartAsync();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        Assert.Equal(HttpStatusCode.OK, dmRes.StatusCode);
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        // The other member learns about the chat live; the outsider hears nothing.
        Assert.Equal(chatId, await ReceiveAsync(created, "chat:updated for new member"));
        await AssertNoReceiveAsync(leaked, "chat:updated for outsider");

        // Mirrors the web client: refetch list, then subscribe to the new group.
        var msg = new TaskCompletionSource<string>();
        bConn.On<JsonElement>("message:new", m => msg.TrySetResult(m.GetProperty("id").GetString()!));
        await bConn.InvokeAsync("Subscribe", new[] { chatId });

        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "first hello", clientMessageId = "nc-1", replyToMessageId = (string?)null });
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var sent = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(sent.RootElement.GetProperty("id").GetString()!, await ReceiveAsync(msg, "first message:new"));
    }

    [Fact]
    public async Task Added_member_is_notified_live()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "am.a");
        var b = await RegisterAsync(factory.CreateClient(), "am.b");
        var cAuth = await RegisterAsync(factory.CreateClient(), "am.c");
        var dAuth = await RegisterAsync(factory.CreateClient(), "am.d");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var grpRes = await client.PostAsJsonAsync("/api/v1/chats/groups",
            new { title = "Team", memberIds = new[] { b.User.Id }, description = (string?)null });
        Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);
        var grp = (await grpRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = grp.RootElement.GetProperty("id").GetString()!;

        await using var cConn = Connect(cAuth.AccessToken);
        await using var dConn = Connect(dAuth.AccessToken);
        var created = new TaskCompletionSource<string>();
        var leaked = new TaskCompletionSource<string>();
        cConn.On<JsonElement>("chat:updated", e => created.TrySetResult(e.GetProperty("chatId").GetString()!));
        dConn.On<JsonElement>("chat:updated", e => leaked.TrySetResult(e.GetProperty("chatId").GetString()!));
        await cConn.StartAsync();
        await dConn.StartAsync();

        var add = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/members", new { userId = cAuth.User.Id });
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        Assert.Equal(chatId, await ReceiveAsync(created, "chat:updated for added member"));
        await AssertNoReceiveAsync(leaked, "chat:updated for outsider");
        _ = (b, cAuth, dAuth);
    }

    [Fact]
    public async Task Reconnect_gap_fills_via_sync_and_live_resumes()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "rc.a");
        var b = await RegisterAsync(factory.CreateClient(), "rc.b");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        var bClient = factory.CreateClient();
        bClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", b.AccessToken);

        await using var bConn = Connect(b.AccessToken);
        var first = new TaskCompletionSource<string>();
        bConn.On<JsonElement>("message:new", m => first.TrySetResult(m.GetProperty("id").GetString()!));
        await bConn.StartAsync();

        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "m1", clientMessageId = "rc-1", replyToMessageId = (string?)null });
        await ReceiveAsync(first, "first live message");

        // Drop the connection; two messages pass while away.
        await bConn.StopAsync();
        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "m2", clientMessageId = "rc-2", replyToMessageId = (string?)null });
        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "m3", clientMessageId = "rc-3", replyToMessageId = (string?)null });

        // Reconnect + gap-fill from the last known seq (mirrors the web client).
        await using var bConn2 = Connect(b.AccessToken);
        var resumed = new TaskCompletionSource<string>();
        bConn2.On<JsonElement>("message:new", m => resumed.TrySetResult(m.GetProperty("id").GetString()!));
        await bConn2.StartAsync();

        var sync = await bClient.GetAsync($"/api/v1/chats/{chatId}/messages/sync?afterSeq=1&limit=100");
        var missed = (await sync.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(2, missed.RootElement.GetArrayLength());

        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "m4", clientMessageId = "rc-4", replyToMessageId = (string?)null });
        Assert.NotEmpty(await ReceiveAsync(resumed, "post-reconnect live message"));
    }
}
