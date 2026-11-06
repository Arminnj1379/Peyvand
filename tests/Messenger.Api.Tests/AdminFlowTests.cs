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
public sealed class AdminFlowTests(MessengerFactory factory)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    private async Task<(HttpClient Client, string AdminToken)> AdminClientAsync(string tag)
    {
        await factory.CleanAsync();

        // Bootstrap a System Administrator straight through the domain (no endpoint exists before one does).
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var passwords = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var now = DateTime.UtcNow;
            var registered = User.Register(
                ObjectId.GenerateNewId().ToString(), $"root.{tag}", "Root", passwords.Hash("password123"),
                UserType.Employee, [Role.SystemAdministrator], null, now);
            Assert.True(registered.IsSuccess);
            await users.AddAsync(registered.Value!, CancellationToken.None);
        }

        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/v1/auth/login",
            new { login = $"root.{tag}", password = "password123", deviceLabel = (string?)null })
            ;
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth.AccessToken);
    }

    [Fact]
    public async Task Admin_creates_employee_and_manages_lifecycle()
    {
        var (client, _) = await AdminClientAsync("life");

        var created = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            username = "agent.one",
            displayName = "Agent One",
            password = "password123",
            roles = new[] { "SupportAgent" },
            phone = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var employee = (await created.Content.ReadFromJsonAsync<UserResponse>(Json))!;
        Assert.Contains("Employee", employee.Roles);
        Assert.Contains("SupportAgent", employee.Roles);

        var list = await client.GetAsync("/api/v1/admin/users?limit=50");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);

        // Deactivate: employee can no longer sign in.
        var off = await client.PatchAsJsonAsync($"/api/v1/admin/users/{employee.Id}/active",
            new { isActive = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);

        var anon = factory.CreateClient();
        var denied = await anon.PostAsJsonAsync("/api/v1/auth/login",
            new { login = "agent.one", password = "password123", deviceLabel = (string?)null })
            ;
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        // Reactivate: sign-in works again.
        var on = await client.PatchAsJsonAsync($"/api/v1/admin/users/{employee.Id}/active",
            new { isActive = true });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        var allowed = await anon.PostAsJsonAsync("/api/v1/auth/login",
            new { login = "agent.one", password = "password123", deviceLabel = (string?)null })
            ;
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        // Strip the agent role.
        var roles = await client.PutAsJsonAsync($"/api/v1/admin/users/{employee.Id}/roles",
            new { roles = new[] { "Employee" } });
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        var updated = (await roles.Content.ReadFromJsonAsync<UserResponse>(Json))!;
        Assert.DoesNotContain("SupportAgent", updated.Roles);
    }

    [Fact]
    public async Task Customer_cannot_touch_admin_endpoints()
    {
        var (client, _) = await AdminClientAsync("cust");

        var anon = factory.CreateClient();
        var reg = await anon.PostAsJsonAsync("/api/v1/auth/register", new
        {
            username = "plain.customer",
            displayName = "Plain",
            password = "password123",
            phone = (string?)null,
            deviceLabel = (string?)null
        });
        var customer = (await reg.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        anon.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", customer.AccessToken);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await anon.GetAsync("/api/v1/admin/users?limit=10")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await anon.PostAsJsonAsync("/api/v1/admin/users", new
            {
                username = "x",
                displayName = "X",
                password = "password123",
                roles = Array.Empty<string>(),
                phone = (string?)null
            })).StatusCode);
        _ = client;
    }

    [Fact]
    public async Task OrgAdmin_cannot_grant_system_admin()
    {
        var (client, _) = await AdminClientAsync("org");

        var org = await client.PostAsJsonAsync("/api/v1/admin/users", new
        {
            username = "org.lead",
            displayName = "Org Lead",
            password = "password123",
            roles = new[] { "OrganizationAdministrator" },
            phone = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, org.StatusCode);

        var orgClient = factory.CreateClient();
        var login = await orgClient.PostAsJsonAsync("/api/v1/auth/login",
            new { login = "org.lead", password = "password123", deviceLabel = (string?)null })
            ;
        var orgAuth = (await login.Content.ReadFromJsonAsync<AuthResponse>(Json))!;
        orgClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", orgAuth.AccessToken);

        var escalate = await orgClient.PostAsJsonAsync("/api/v1/admin/users", new
        {
            username = "evil.admin",
            displayName = "Evil",
            password = "password123",
            roles = new[] { "SystemAdministrator" },
            phone = (string?)null
        });
        Assert.Equal(HttpStatusCode.Forbidden, escalate.StatusCode);
    }
}
