using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;

namespace Messenger.Api.Tests;

/// <summary>
/// SignalR fan-out for the messaging feature set (edit/delete/reactions/pins):
/// every mutation persists via REST first, then the endpoint broadcasts only
/// to the conversation's <c>chat:{id}</c> group — members hear it, outsiders
/// stay silent. LongPolling transport (the in-memory test server has no
/// WebSockets); same hub pipeline as production.
/// </summary>
[Collection("auth")]
public sealed class MessageEventTests(MessengerFactory factory)
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
                o.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
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
    public async Task Edited_deleted_and_reacted_events_reach_members()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "ev.a");
        var b = await RegisterAsync(factory.CreateClient(), "ev.b");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        Assert.Equal(HttpStatusCode.OK, dmRes.StatusCode);
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "original", clientMessageId = "ev-1", replyToMessageId = (string?)null });
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);
        var sent = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var messageId = sent.RootElement.GetProperty("id").GetString()!;

        await using var bConn = Connect(b.AccessToken);
        var edited = new TaskCompletionSource<JsonElement>();
        var reacted = new TaskCompletionSource<JsonElement>();
        var deleted = new TaskCompletionSource<JsonElement>();
        bConn.On<JsonElement>("message:edited", m => edited.TrySetResult(m));
        bConn.On<JsonElement>("message:reacted", m => reacted.TrySetResult(m));
        bConn.On<JsonElement>("message:deleted", m => deleted.TrySetResult(m));
        await bConn.StartAsync();

        var edit = await client.PatchAsJsonAsync($"/api/v1/messages/{messageId}", new { body = "updated" });
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        var editedEvt = await ReceiveAsync(edited, "message:edited");
        Assert.Equal(messageId, editedEvt.GetProperty("id").GetString());
        Assert.True(editedEvt.GetProperty("isEdited").GetBoolean());

        var bClient = factory.CreateClient();
        bClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", b.AccessToken);
        var react = await bClient.PostAsJsonAsync($"/api/v1/messages/{messageId}/reactions", new { emoji = "👍" });
        Assert.Equal(HttpStatusCode.OK, react.StatusCode);
        var reactedEvt = await ReceiveAsync(reacted, "message:reacted");
        Assert.Equal(messageId, reactedEvt.GetProperty("id").GetString());
        Assert.Equal(1, reactedEvt.GetProperty("reactions").GetArrayLength());

        var del = await client.DeleteAsync($"/api/v1/messages/{messageId}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        var deletedEvt = await ReceiveAsync(deleted, "message:deleted");
        Assert.Equal(messageId, deletedEvt.GetProperty("id").GetString());
        Assert.True(deletedEvt.GetProperty("isDeleted").GetBoolean());
    }

    [Fact]
    public async Task Mutation_events_stay_silent_for_outsider()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "eo.a");
        var b = await RegisterAsync(factory.CreateClient(), "eo.b");
        var cAuth = await RegisterAsync(factory.CreateClient(), "eo.c");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var dm = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = dm.RootElement.GetProperty("id").GetString()!;

        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "private", clientMessageId = "eo-1", replyToMessageId = (string?)null });
        var sent = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var messageId = sent.RootElement.GetProperty("id").GetString()!;

        await using var cConn = Connect(cAuth.AccessToken);
        var leakedEdit = new TaskCompletionSource<string>();
        var leakedReact = new TaskCompletionSource<string>();
        var leakedDelete = new TaskCompletionSource<string>();
        cConn.On<JsonElement>("message:edited", _ => leakedEdit.TrySetResult("x"));
        cConn.On<JsonElement>("message:reacted", _ => leakedReact.TrySetResult("x"));
        cConn.On<JsonElement>("message:deleted", _ => leakedDelete.TrySetResult("x"));
        await cConn.StartAsync();

        Assert.Equal(HttpStatusCode.OK,
            (await client.PatchAsJsonAsync($"/api/v1/messages/{messageId}", new { body = "v2" })).StatusCode);
        var bClient = factory.CreateClient();
        bClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", b.AccessToken);
        Assert.Equal(HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync($"/api/v1/messages/{messageId}/reactions", new { emoji = "❤️" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.DeleteAsync($"/api/v1/messages/{messageId}")).StatusCode);

        await AssertNoReceiveAsync(leakedEdit, "message:edited for outsider");
        await AssertNoReceiveAsync(leakedReact, "message:reacted for outsider");
        await AssertNoReceiveAsync(leakedDelete, "message:deleted for outsider");
        _ = cAuth;
    }

    [Fact]
    public async Task Pin_and_unpin_fan_out_chat_updated_to_members_only()
    {
        var client = await CleanClientAsync();
        var owner = await RegisterAsync(client, "pn.owner");
        var memberAuth = await RegisterAsync(factory.CreateClient(), "pn.member");
        var outsiderAuth = await RegisterAsync(factory.CreateClient(), "pn.out");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var grpRes = await client.PostAsJsonAsync("/api/v1/chats/groups",
            new { title = "Pins", memberIds = new[] { memberAuth.User.Id }, description = (string?)null });
        Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);
        var grp = (await grpRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = grp.RootElement.GetProperty("id").GetString()!;

        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "announce", clientMessageId = "pn-1", replyToMessageId = (string?)null });
        var sent = (await send.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var messageId = sent.RootElement.GetProperty("id").GetString()!;

        await using var memberConn = Connect(memberAuth.AccessToken);
        await using var outsiderConn = Connect(outsiderAuth.AccessToken);
        var memberNotified = new TaskCompletionSource<string>();
        var outsiderLeaked = new TaskCompletionSource<string>();
        memberConn.On<JsonElement>("chat:updated", e => memberNotified.TrySetResult(e.GetProperty("chatId").GetString()!));
        outsiderConn.On<JsonElement>("chat:updated", e => outsiderLeaked.TrySetResult(e.GetProperty("chatId").GetString()!));
        await memberConn.StartAsync();
        await outsiderConn.StartAsync();

        var pin = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/pins", new { messageId });
        Assert.Equal(HttpStatusCode.OK, pin.StatusCode);
        Assert.Equal(chatId, await ReceiveAsync(memberNotified, "chat:updated on pin"));
        await AssertNoReceiveAsync(outsiderLeaked, "chat:updated on pin for outsider");

        // Non-admin pin attempt is rejected and broadcasts nothing.
        var memberClient = factory.CreateClient();
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/pins", new { messageId })).StatusCode);

        var unpin = await client.DeleteAsync($"/api/v1/chats/{chatId}/pins/{messageId}");
        Assert.Equal(HttpStatusCode.OK, unpin.StatusCode);

        var pins = await client.GetAsync($"/api/v1/chats/{chatId}/pins");
        var pinned = (await pins.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(0, pinned.RootElement.GetArrayLength());
        _ = outsiderAuth;
    }

    [Fact]
    public async Task Group_deleted_notifies_members_live()
    {
        var client = await CleanClientAsync();
        var owner = await RegisterAsync(client, "gd.owner");
        var memberAuth = await RegisterAsync(factory.CreateClient(), "gd.member");
        var outsiderAuth = await RegisterAsync(factory.CreateClient(), "gd.out");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", owner.AccessToken);
        var grpRes = await client.PostAsJsonAsync("/api/v1/chats/groups",
            new { title = "Doomed", memberIds = new[] { memberAuth.User.Id }, description = (string?)null });
        Assert.Equal(HttpStatusCode.Created, grpRes.StatusCode);
        var grp = (await grpRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var chatId = grp.RootElement.GetProperty("id").GetString()!;

        // Promote the member to admin: admins still cannot delete (owner-only).
        Assert.Equal(HttpStatusCode.OK,
            (await client.PutAsJsonAsync($"/api/v1/chats/{chatId}/members/{memberAuth.User.Id}/role",
                new { role = "Admin" })).StatusCode);
        var memberClient = factory.CreateClient();
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.DeleteAsync($"/api/v1/chats/{chatId}")).StatusCode);

        await using var memberConn = Connect(memberAuth.AccessToken);
        await using var outsiderConn = Connect(outsiderAuth.AccessToken);
        var memberNotified = new TaskCompletionSource<string>();
        var outsiderLeaked = new TaskCompletionSource<string>();
        memberConn.On<JsonElement>("chat:updated", e => memberNotified.TrySetResult(e.GetProperty("chatId").GetString()!));
        outsiderConn.On<JsonElement>("chat:updated", e => outsiderLeaked.TrySetResult(e.GetProperty("chatId").GetString()!));
        await memberConn.StartAsync();
        await outsiderConn.StartAsync();

        var del = await client.DeleteAsync($"/api/v1/chats/{chatId}");
        Assert.Equal(HttpStatusCode.OK, del.StatusCode);
        Assert.Equal(chatId, await ReceiveAsync(memberNotified, "chat:updated on delete"));
        await AssertNoReceiveAsync(outsiderLeaked, "chat:updated on delete for outsider");

        // The deleted group vanishes: get/history read as not-found, lists exclude it.
        Assert.Equal(HttpStatusCode.NotFound,
            (await memberClient.GetAsync($"/api/v1/chats/{chatId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await memberClient.GetAsync($"/api/v1/chats/{chatId}/messages?limit=30")).StatusCode);
        var listRes = await memberClient.GetAsync("/api/v1/chats?limit=50");
        var list = (await listRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.DoesNotContain(list.RootElement.EnumerateArray(),
            c => c.GetProperty("id").GetString() == chatId);
        _ = outsiderAuth;
    }
}
