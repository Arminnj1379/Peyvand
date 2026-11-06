using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Messenger.Api.Tests;

[Collection("auth")]
public sealed class CallFlowTests(MessengerFactory factory)
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

    private static async Task<string> StartDmAsync(HttpClient client, string otherUserId)
    {
        var res = await client.PostAsJsonAsync("/api/v1/chats/dm", new { otherUserId });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement
            .GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Full_call_flow_with_livekit_token()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "call.a");
        var b = await RegisterAsync(factory.CreateClient(), "call.b");

        AuthAs(client, a.AccessToken);
        var chatId = await StartDmAsync(client, b.User.Id);

        // Start a video call.
        var start = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/calls", new { mediaType = "Video" });
        Assert.Equal(HttpStatusCode.Created, start.StatusCode);
        var call = (await start.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement;
        var callId = call.GetProperty("id").GetString()!;
        Assert.Equal("Ringing", call.GetProperty("status").GetString());
        Assert.Equal("Video", call.GetProperty("mediaType").GetString());

        // Second call while live → 409.
        var busy = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/calls", new { mediaType = "Audio" });
        Assert.Equal(HttpStatusCode.Conflict, busy.StatusCode);

        // Callee accepts, then both join media (tokens minted server-side).
        var bClient = factory.CreateClient();
        AuthAs(bClient, b.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await bClient.PostAsJsonAsync($"/api/v1/calls/{callId}/accept", new { })).StatusCode);

        var joinB = await bClient.PostAsJsonAsync($"/api/v1/calls/{callId}/join", new { });
        Assert.Equal(HttpStatusCode.OK, joinB.StatusCode);
        var joinDoc = (await joinB.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement;
        Assert.Equal("wss://livekit.test", joinDoc.GetProperty("url").GetString());
        Assert.Equal("Active", joinDoc.GetProperty("call").GetProperty("status").GetString());

        // The token is a signed LiveKit JWT: issuer = API key, room grant matches.
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(joinDoc.GetProperty("token").GetString()!);
        Assert.Equal("testkey", jwt.Issuer);
        Assert.Equal(b.User.Id, jwt.Subject);
        var video = jwt.Claims.First(c => c.Type == "video").Value;
        Assert.Contains(call.GetProperty("roomName").GetString()!, video);
        Assert.Contains("roomJoin", video);

        var joinA = await client.PostAsJsonAsync($"/api/v1/calls/{callId}/join", new { });
        Assert.Equal(HttpStatusCode.OK, joinA.StatusCode);

        // History shows the call; either party hangs up.
        var history = await client.GetAsync($"/api/v1/chats/{chatId}/calls?limit=10");
        var list = (await history.Content.ReadFromJsonAsync<JsonDocument>(Json))!;
        Assert.Equal(1, list.RootElement.GetArrayLength());

        var end = await client.PostAsJsonAsync($"/api/v1/calls/{callId}/end", new { });
        Assert.Equal(HttpStatusCode.OK, end.StatusCode);
        var ended = (await end.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement;
        Assert.Equal("Ended", ended.GetProperty("status").GetString());
        Assert.Equal("Completed", ended.GetProperty("endReason").GetString());

        // Ended calls reject further joins.
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/v1/calls/{callId}/join", new { })).StatusCode);

        // A new call can start after the previous one ended.
        var again = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/calls", new { mediaType = "Audio" });
        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Call_authorization_rules()
    {
        var client = await CleanClientAsync();
        var a = await RegisterAsync(client, "authz.a");
        var b = await RegisterAsync(factory.CreateClient(), "authz.b");
        var outsider = await RegisterAsync(factory.CreateClient(), "authz.out");

        AuthAs(client, a.AccessToken);
        var chatId = await StartDmAsync(client, b.User.Id);
        var start = await client.PostAsJsonAsync($"/api/v1/chats/{chatId}/calls", new { mediaType = "Audio" });
        var callId = (await start.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement
            .GetProperty("id").GetString()!;

        var outClient = factory.CreateClient();
        AuthAs(outClient, outsider.AccessToken);

        // Outsider: no read, no join, no end (neutral 403, never 500).
        Assert.Equal(HttpStatusCode.Forbidden, (await outClient.GetAsync($"/api/v1/calls/{callId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outClient.PostAsJsonAsync($"/api/v1/calls/{callId}/join", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outClient.PostAsJsonAsync($"/api/v1/calls/{callId}/end", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await outClient.GetAsync($"/api/v1/chats/{chatId}/calls?limit=10")).StatusCode);

        // Group: plain member cannot end someone else's call for everyone.
        var c = await RegisterAsync(factory.CreateClient(), "authz.c");
        var groupRes = await client.PostAsJsonAsync("/api/v1/chats/groups",
            new { title = "Team", memberIds = new[] { a.User.Id, b.User.Id, c.User.Id }, description = (string?)null });
        var groupId = (await groupRes.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement
            .GetProperty("id").GetString()!;
        var gStart = await client.PostAsJsonAsync($"/api/v1/chats/{groupId}/calls", new { mediaType = "Audio" });
        var gCallId = (await gStart.Content.ReadFromJsonAsync<JsonDocument>(Json))!.RootElement
            .GetProperty("id").GetString()!;

        var cClient = factory.CreateClient();
        AuthAs(cClient, c.AccessToken);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await cClient.PostAsJsonAsync($"/api/v1/calls/{gCallId}/end", new { })).StatusCode);

        // But the member can still accept + join (ringing is for all members).
        Assert.Equal(HttpStatusCode.OK,
            (await cClient.PostAsJsonAsync($"/api/v1/calls/{gCallId}/accept", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await cClient.PostAsJsonAsync($"/api/v1/calls/{gCallId}/join", new { })).StatusCode);
    }
}
