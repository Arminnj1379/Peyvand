using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Messenger.Api.Tests;

[Collection("auth")]
public sealed class AdvancedFlowTests(MessengerFactory factory)
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

    private static async Task<string> SendAsync(HttpClient client, string chatId, string body, string key, string? replyTo = null)
    {
        var m = await PostAsync(client, $"/api/v1/chats/{chatId}/messages",
            new { body, clientMessageId = key, replyToMessageId = replyTo }, HttpStatusCode.Created);
        return m.GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Invite_rotate_join_revoke_flow()
    {
        var client = await CleanClientAsync();
        var owner = await RegisterAsync(client, "adv.owner");
        var joinerAuth = await RegisterAsync(factory.CreateClient(), "adv.joiner");

        AuthAs(client, owner.AccessToken);
        var helper = await RegisterAsync(factory.CreateClient(), "adv.helper");
        var grp2 = await PostAsync(client, "/api/v1/chats/groups",
            new { title = "Invite Group", memberIds = new[] { helper.User.Id }, description = (string?)null },
            HttpStatusCode.Created);
        var chatId = grp2.GetProperty("id").GetString()!;

        // Non-admin cannot rotate.
        var helperClient = factory.CreateClient();
        AuthAs(helperClient, helper.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await helperClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/invite/rotate", new { })).StatusCode);

        // Admin rotates; any member can read the link.
        var code = await PostAsync(client, $"/api/v1/chats/{chatId}/invite/rotate", new { }, HttpStatusCode.OK);
        Assert.NotEmpty(code.GetString()!);
        var seen = await helperClient.GetAsync($"/api/v1/chats/{chatId}/invite");
        Assert.Equal(HttpStatusCode.OK, seen.StatusCode);

        // Joiner redeems the link and can post.
        var joinerClient = factory.CreateClient();
        AuthAs(joinerClient, joinerAuth.AccessToken);
        var joined = await PostAsync(joinerClient, "/api/v1/chats/join",
            new { code = code.GetString() }, HttpStatusCode.OK);
        Assert.Equal(chatId, joined.GetProperty("id").GetString());
        await SendAsync(joinerClient, chatId, "hello via invite", "inv-1");

        // Invalid codes are indistinguishable from revoked ones.
        Assert.Equal(HttpStatusCode.NotFound,
            (await joinerClient.PostAsJsonAsync("/api/v1/chats/join", new { code = "deadbeef00" })).StatusCode);

        // Revoke kills the link.
        Assert.Equal(HttpStatusCode.OK,
            (await client.DeleteAsync($"/api/v1/chats/{chatId}/invite")).StatusCode);
        var lateAuth = await RegisterAsync(factory.CreateClient(), "adv.late");
        var lateClient = factory.CreateClient();
        AuthAs(lateClient, lateAuth.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound,
            (await lateClient.PostAsJsonAsync("/api/v1/chats/join", new { code = code.GetString() })).StatusCode);
    }

    [Fact]
    public async Task Public_channel_subscribe_and_visibility_flow()
    {
        var client = await CleanClientAsync();
        var owner = await RegisterAsync(client, "pub.owner");
        var fanAuth = await RegisterAsync(factory.CreateClient(), "pub.fan");

        AuthAs(client, owner.AccessToken);
        var ch = await PostAsync(client, "/api/v1/chats/channels",
            new { title = "Open News", description = (string?)null, subscriberIds = Array.Empty<string>(), isPublic = true, username = "opennews" },
            HttpStatusCode.Created);
        var chatId = ch.GetProperty("id").GetString()!;
        Assert.True(ch.GetProperty("isPublic").GetBoolean());
        Assert.Equal("opennews", ch.GetProperty("username").GetString());

        // Username conflicts (case-insensitive).
        Assert.Equal(HttpStatusCode.Conflict,
            (await client.PostAsync("/api/v1/chats/channels",
                JsonContent.Create(new { title = "Copy", description = (string?)null, subscriberIds = Array.Empty<string>(), isPublic = true, username = "OpenNews" }))).StatusCode);

        // Public card resolves without membership.
        var fanClient = factory.CreateClient();
        AuthAs(fanClient, fanAuth.AccessToken);
        var card = await fanClient.GetAsync("/api/v1/chats/public/opennews");
        Assert.Equal(HttpStatusCode.OK, card.StatusCode);

        // Subscribe, post-denied (subscriber), react-allowed, unsubscribe.
        var sub = await PostAsync(fanClient, $"/api/v1/chats/{chatId}/subscribe", new { }, HttpStatusCode.OK);
        Assert.Equal(chatId, sub.GetProperty("id").GetString());
        Assert.Equal(HttpStatusCode.Conflict,
            (await fanClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/subscribe", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await fanClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
                new { body = "nope", clientMessageId = "k-s", replyToMessageId = (string?)null })).StatusCode);

        var msgId = await SendAsync(client, chatId, "broadcast", "k-b");
        var reacted = await PostAsync(fanClient, $"/api/v1/messages/{msgId}/reactions",
            new { emoji = "👍" }, HttpStatusCode.OK);
        Assert.Equal(1, reacted.GetProperty("reactions").GetArrayLength());

        Assert.Equal(HttpStatusCode.OK,
            (await fanClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/unsubscribe", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await fanClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/unsubscribe", new { })).StatusCode);

        // Going private hides the card and blocks subscribes.
        var vis = await client.PutAsJsonAsync($"/api/v1/chats/{chatId}/visibility",
            new { isPublic = false, username = (string?)null });
        Assert.Equal(HttpStatusCode.OK, vis.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await fanClient.GetAsync("/api/v1/chats/public/opennews")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await fanClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/subscribe", new { })).StatusCode);
    }

    [Fact]
    public async Task Reactions_threads_and_bulk_delete_flow()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "rt.a");
        var bAuth = await RegisterAsync(factory.CreateClient(), "rt.b");

        AuthAs(client, a.AccessToken);
        var grp = await PostAsync(client, "/api/v1/chats/groups",
            new { title = "Thread Group", memberIds = new[] { bAuth.User.Id }, description = (string?)null },
            HttpStatusCode.Created);
        var chatId = grp.GetProperty("id").GetString()!;

        var parentId = await SendAsync(client, chatId, "question", "k-1");
        var bClient = factory.CreateClient();
        AuthAs(bClient, bAuth.AccessToken);
        await SendAsync(bClient, chatId, "answer", "k-2", parentId);

        // Thread lists the child; parent carries the count.
        var thread = await client.GetAsync($"/api/v1/messages/{parentId}/replies?limit=10");
        Assert.Equal(HttpStatusCode.OK, thread.StatusCode);
        var replies = (await thread.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(1, replies.RootElement.GetArrayLength());

        var hist = await client.GetAsync($"/api/v1/chats/{chatId}/messages?limit=10");
        var page = (await hist.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        var parent = page.RootElement.GetProperty("items").EnumerateArray()
            .First(i => i.GetProperty("id").GetString() == parentId);
        Assert.Equal(1, parent.GetProperty("replyCount").GetInt32());

        // Toggle on/off.
        var r1 = await PostAsync(bClient, $"/api/v1/messages/{parentId}/reactions",
            new { emoji = "❤️" }, HttpStatusCode.OK);
        Assert.Equal(1, r1.GetProperty("reactions")[0].GetProperty("count").GetInt32());
        var r2 = await PostAsync(bClient, $"/api/v1/messages/{parentId}/reactions",
            new { emoji = "❤️" }, HttpStatusCode.OK);
        Assert.Equal(0, r2.GetProperty("reactions").GetArrayLength());

        // Outsider cannot react.
        var cAuth = await RegisterAsync(factory.CreateClient(), "rt.c");
        var cClient = factory.CreateClient();
        AuthAs(cClient, cAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cClient.PostAsJsonAsync($"/api/v1/messages/{parentId}/reactions", new { emoji = "👍" })).StatusCode);

        // Bulk delete by admin removes both; selection of deleted ids succeeds empty.
        var bulk = await PostAsync(client, "/api/v1/messages/bulk-delete",
            new { messageIds = new[] { parentId } }, HttpStatusCode.OK);
        Assert.Equal(1, bulk.GetArrayLength());
        var bulk2 = await PostAsync(client, "/api/v1/messages/bulk-delete",
            new { messageIds = new[] { parentId } }, HttpStatusCode.OK);
        Assert.Equal(0, bulk2.GetArrayLength());
    }

    [Fact]
    public async Task Preferences_mute_archive_unread_flow()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "pf.a");
        var bAuth = await RegisterAsync(factory.CreateClient(), "pf.b");

        AuthAs(client, a.AccessToken);
        var grp = await PostAsync(client, "/api/v1/chats/groups",
            new { title = "Prefs", memberIds = new[] { bAuth.User.Id }, description = (string?)null },
            HttpStatusCode.Created);
        var chatId = grp.GetProperty("id").GetString()!;
        await SendAsync(client, chatId, "one", "k-1");
        await SendAsync(client, chatId, "two", "k-2");

        var bClient = factory.CreateClient();
        AuthAs(bClient, bAuth.AccessToken);

        var muted = await PostAsync(bClient, $"/api/v1/chats/{chatId}/mute", new { value = true }, HttpStatusCode.OK);
        Assert.True(muted.GetProperty("isMuted").GetBoolean());

        var archived = await PostAsync(bClient, $"/api/v1/chats/{chatId}/archive", new { value = true }, HttpStatusCode.OK);
        Assert.True(archived.GetProperty("isArchived").GetBoolean());

        // Caller-relative: the other member sees defaults.
        var peer = await client.GetAsync($"/api/v1/chats/{chatId}");
        var peerDoc = (await peer.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.False(peerDoc.RootElement.GetProperty("isMuted").GetBoolean());
        Assert.False(peerDoc.RootElement.GetProperty("isArchived").GetBoolean());

        // Read all, then mark unread from seq 2.
        Assert.Equal(HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync($"/api/v1/chats/{chatId}/read", new { seq = 2 })).StatusCode);
        var unread = await PostAsync(bClient, $"/api/v1/chats/{chatId}/unread", new { seq = 2 }, HttpStatusCode.OK);
        Assert.Equal(1, unread.GetProperty("lastReadSeq").GetInt64());
        Assert.Equal(1, unread.GetProperty("unreadCount").GetInt32());

        // Unarchive/unmute round-trip.
        Assert.False((await PostAsync(bClient, $"/api/v1/chats/{chatId}/archive", new { value = false }, HttpStatusCode.OK))
            .GetProperty("isArchived").GetBoolean());
        Assert.False((await PostAsync(bClient, $"/api/v1/chats/{chatId}/mute", new { value = false }, HttpStatusCode.OK))
            .GetProperty("isMuted").GetBoolean());
    }

    [Fact]
    public async Task Group_profile_and_member_invite_policy()
    {
        var client = await CleanClientAsync();
        var owner = await RegisterAsync(client, "gp.owner");
        var memberAuth = await RegisterAsync(factory.CreateClient(), "gp.member");
        var strangerAuth = await RegisterAsync(factory.CreateClient(), "gp.stranger");

        AuthAs(client, owner.AccessToken);
        var grp = await PostAsync(client, "/api/v1/chats/groups",
            new { title = "Policy", memberIds = new[] { memberAuth.User.Id }, description = (string?)null },
            HttpStatusCode.Created);
        var chatId = grp.GetProperty("id").GetString()!;

        // Member cannot edit the profile.
        var memberClient = factory.CreateClient();
        AuthAs(memberClient, memberAuth.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await memberClient.PatchAsJsonAsync($"/api/v1/chats/{chatId}",
                new { title = "Hijacked", description = (string?)null, membersCanInvite = (bool?)null })).StatusCode);

        // Owner renames and enables member invites.
        var patch = await memberClient.PatchAsJsonAsync($"/api/v1/chats/{chatId}",
            new { title = "Hijacked", description = (string?)null, membersCanInvite = (bool?)null });
        _ = patch;
        var updated = await client.PatchAsJsonAsync($"/api/v1/chats/{chatId}",
            new { title = "Renamed", description = "About us", membersCanInvite = (bool?)true });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var doc = (await updated.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal("Renamed", doc.RootElement.GetProperty("title").GetString());
        Assert.True(doc.RootElement.GetProperty("membersCanInvite").GetBoolean());

        // Member can now invite the stranger.
        var added = await PostAsync(memberClient, $"/api/v1/chats/{chatId}/members",
            new { userId = strangerAuth.User.Id }, HttpStatusCode.OK);
        Assert.Equal(3, added.GetProperty("memberCount").GetInt32());
    }

    [Fact]
    public async Task Block_stops_dm_create_and_send()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "bl.a");
        var bAuth = await RegisterAsync(factory.CreateClient(), "bl.b");

        // b blocks a: a can no longer start a DM.
        var bClient = factory.CreateClient();
        AuthAs(bClient, bAuth.AccessToken);
        Assert.Equal(HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync($"/api/v1/users/{a.User.Id}/block", new { })).StatusCode);

        AuthAs(client, a.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = bAuth.User.Id })).StatusCode);

        // Unblock, create the DM, then re-block: sends from the blocked side fail.
        Assert.Equal(HttpStatusCode.OK,
            (await bClient.DeleteAsync($"/api/v1/users/{a.User.Id}/block")).StatusCode);
        var dm = await PostAsync(client, "/api/v1/chats/dm",
            new { otherUserId = bAuth.User.Id }, HttpStatusCode.OK);
        var chatId = dm.GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.OK,
            (await bClient.PostAsJsonAsync($"/api/v1/users/{a.User.Id}/block", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
                new { body = "blocked?", clientMessageId = "k-bl", replyToMessageId = (string?)null })).StatusCode);
    }
}
