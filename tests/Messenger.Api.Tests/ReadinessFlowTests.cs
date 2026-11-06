using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Messenger.Application.Abstractions;
using Messenger.Domain.Users;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;

namespace Messenger.Api.Tests;

[Collection("auth")]
public sealed class ReadinessFlowTests(MessengerFactory factory)
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

    private async Task<(HttpClient Client, AuthResponse Auth)> AgentClientAsync(string tag)
    {
        await factory.CleanAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var passwords = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var registered = User.Register(
                ObjectId.GenerateNewId().ToString(), $"agent.{tag}", "Agent", passwords.Hash("password123"),
                UserType.Employee, [Role.SupportAgent], null, DateTime.UtcNow);
            Assert.True(registered.IsSuccess);
            await users.AddAsync(registered.Value!, CancellationToken.None);
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { login = $"agent.{tag}", password = "password123", deviceLabel = (string?)null });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private static void AuthAs(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    [Fact]
    public async Task Search_messages_and_chats()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "find.a");
        var b = await RegisterAsync(factory.CreateClient(), "find.b");

        AuthAs(client, a.AccessToken);
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var chatId = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!
            .RootElement.GetProperty("id").GetString()!;
        var send = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "the deployment runbook lives here", clientMessageId = "s-1", replyToMessageId = (string?)null, fileId = (string?)null });
        Assert.Equal(HttpStatusCode.Created, send.StatusCode);

        var found = await client.GetAsync("/api/v1/messages/search?q=runbook&limit=10");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var items = (await found.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(1, items.RootElement.GetArrayLength());

        var chats = await client.GetAsync($"/api/v1/chats/search?q={b.User.Username}&limit=10");
        Assert.Equal(HttpStatusCode.OK, chats.StatusCode);
        Assert.Equal(1, (await chats.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement.GetArrayLength());

        // Validation still applies.
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/v1/messages/search?q=x&limit=10")).StatusCode);
    }

    [Fact]
    public async Task Settings_and_notifications()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "pref.a");
        var b = await RegisterAsync(factory.CreateClient(), "pref.b");

        AuthAs(client, a.AccessToken);
        var settings = await client.GetAsync("/api/v1/users/me/settings");
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        Assert.Equal("system", (await settings.Content.ReadFromJsonAsync<JsonDocument>(Json))!
            .RootElement.GetProperty("theme").GetString());

        var update = await client.PatchAsJsonAsync("/api/v1/users/me/settings",
            new { theme = "dark", notificationsEnabled = false });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var bad = await client.PatchAsJsonAsync("/api/v1/users/me/settings",
            new { theme = "neon", notificationsEnabled = true });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        // Peer message → notification row for the recipient only.
        var dmRes = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId = b.User.Id });
        var chatId = (await dmRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!
            .RootElement.GetProperty("id").GetString()!;
        await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/messages",
            new { body = "hello", clientMessageId = "n-1", replyToMessageId = (string?)null, fileId = (string?)null });

        var bClient = factory.CreateClient();
        AuthAs(bClient, b.AccessToken);
        var inbox = await bClient.GetAsync("/api/v1/notifications?limit=10");
        Assert.Equal(HttpStatusCode.OK, inbox.StatusCode);
        var rows = (await inbox.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(1, rows.RootElement.GetArrayLength());
        Assert.Equal(1, rows.RootElement[0].GetProperty("unreadCount").GetInt32());
    }

    [Fact]
    public async Task Support_ticket_lifecycle_with_assignment_chat()
    {
        var (agentClient, agent) = await AgentClientAsync("t1");
        var customer = await RegisterAsync(factory.CreateClient(), "sup.customer");
        var customerClient = factory.CreateClient();
        AuthAs(customerClient, customer.AccessToken);

        var open = await customerClient.PostAsJsonAsync("/api/v1/support/tickets",
            new { subject = "Cannot log in", description = "App crashes" });
        Assert.Equal(HttpStatusCode.Created, open.StatusCode);
        var ticket = (await open.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement;
        var ticketId = ticket.GetProperty("id").GetString()!;
        Assert.Equal("Open", ticket.GetProperty("status").GetString());

        // Another customer cannot see it.
        var stranger = await RegisterAsync(factory.CreateClient(), "sup.stranger");
        var strangerClient = factory.CreateClient();
        AuthAs(strangerClient, stranger.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden, (await strangerClient.GetAsync($"/api/v1/support/tickets/{ticketId}")).StatusCode);

        // Agent queue sees it; self-assign spins up the conversation.
        var queue = await agentClient.GetAsync("/api/v1/support/tickets?status=Open&limit=10");
        Assert.Equal(1, (await queue.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement.GetArrayLength());

        var assign = await agentClient.PostAsJsonAsync($"/api/v1/support/tickets/{ticketId}/assign", new { agentId = (string?)null });
        Assert.Equal(HttpStatusCode.OK, assign.StatusCode);
        var assigned = (await assign.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement;
        Assert.Equal("Assigned", assigned.GetProperty("status").GetString());
        Assert.Equal(agent.User.Id, assigned.GetProperty("assigneeId").GetString());
        var chatId = assigned.GetProperty("chatId").GetString();
        Assert.False(string.IsNullOrEmpty(chatId));

        // Both parties are members of the support conversation.
        Assert.Equal(HttpStatusCode.OK, (await customerClient.GetAsync($"/api/v1/chats/{chatId}")).StatusCode);

        foreach (var status in new[] { "InProgress", "Resolved" })
        {
            var move = await agentClient.PostAsJsonAsync($"/api/v1/support/tickets/{ticketId}/status",
                new { status });
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        }

        var close = await customerClient.PostAsJsonAsync($"/api/v1/support/tickets/{ticketId}/status",
            new { status = "Closed" });
        Assert.Equal(HttpStatusCode.OK, close.StatusCode);

        // Terminal: no further transitions.
        var again = await agentClient.PostAsJsonAsync($"/api/v1/support/tickets/{ticketId}/status",
            new { status = "InProgress" });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Rate_limiting_returns_429_with_code()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "rl.a");
        AuthAs(client, a.AccessToken);

        // The generous global window (600/min) must not trip on a handful of calls.
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        }
    }
}
