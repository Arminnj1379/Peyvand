using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Messenger.Api.Tests;

[Collection("auth")]
public sealed class AuthFlowTests(MessengerFactory factory)
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
            displayName = "Test User",
            password = "password123",
            phone = (string?)null,
            deviceLabel = "test"
        });
        Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
    }

    [Fact]
    public async Task Register_login_me_refresh_revoke_flow()
    {
        var client = await CleanClientAsync();
        var auth = await RegisterAsync(client, "flow.user");
        Assert.NotEmpty(auth.AccessToken);
        Assert.NotEmpty(auth.RefreshToken);
        Assert.Contains("Customer", auth.User.Roles);

        // Authenticated read.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var me = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // Rotation: new pair works, old refresh token is retired.
        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        Assert.NotEqual(auth.RefreshToken, rotated.RefreshToken);

        // Replaying the stolen first token nukes every session.
        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var latest = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = rotated.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, latest.StatusCode);
    }

    [Fact]
    public async Task Logout_revokes_only_the_current_session()
    {
        var client = await CleanClientAsync();
        var first = await RegisterAsync(client, "logout.user");

        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { login = "logout.user", password = "password123", deviceLabel = "second" });
        var second = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);

        // First session dead, second alive.
        var dead = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = first.RefreshToken });
        Assert.Equal(HttpStatusCode.Unauthorized, dead.StatusCode);

        var alive = await client.PostAsJsonAsync("/api/v1/auth/refresh",
            new { refreshToken = second.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
    }

    [Fact]
    public async Task Duplicate_registration_returns_409()
    {
        var client = await CleanClientAsync();
        await RegisterAsync(client, "dup.user");

        var again = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username = "DUP.USER",
            displayName = "Dup",
            password = "password123",
            phone = (string?)null,
            deviceLabel = (string?)null
        });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task Validation_returns_400()
    {
        var client = await CleanClientAsync();
        var res = await client.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username = "x",
            displayName = "X",
            password = "short",
            phone = (string?)null,
            deviceLabel = (string?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Login_failures_do_not_enumerate_users()
    {
        var client = await CleanClientAsync();
        await RegisterAsync(client, "enum.user");

        async Task<string> TitleOfAsync(string login, string password)
        {
            var res = await client.PostAsJsonAsync("/api/v1/auth/login",
                new { login, password, deviceLabel = (string?)null });
            Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            var doc = await res.Content.ReadFromJsonAsync<JsonDocument>();
            return doc!.RootElement.GetProperty("title").GetString()!;
        }

        var unknown = await TitleOfAsync("ghost.user", "password123");
        var wrong = await TitleOfAsync("enum.user", "wrong-password");
        Assert.Equal(unknown, wrong);
    }

    [Fact]
    public async Task Unauthenticated_profile_returns_401()
    {
        var client = await CleanClientAsync();
        var res = await client.GetAsync("/api/v1/users/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }
}
