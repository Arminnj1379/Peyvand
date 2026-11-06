using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;

using Messenger.Application.Abstractions;

namespace Messenger.Api.Tests;

/// <summary>
/// Real stack (API + MongoDB on localhost), isolated database per run.
/// Tests in the "auth" collection run sequentially and clean collections first.
/// </summary>
public sealed class MessengerFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestDb = "messenger_auth_tests";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // directConnection: the shared local mongo advertises a docker-only
                // replica-set hostname; talk to it directly (no transactions used).
                ["Mongo:ConnectionString"] = "mongodb://localhost:27017?directConnection=true",
                ["Mongo:DatabaseName"] = TestDb,
                ["Jwt:Issuer"] = "test",
                ["Jwt:Audience"] = "test",
                ["Jwt:SigningKey"] = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
                ["SeedAdmin:Enabled"] = "false",
                // Exercise the middleware path with headroom: the whole
                // sequential suite shares one localhost IP partition.
                ["RateLimit:AuthPermitLimit"] = "10000",
                ["RateLimit:ApiPermitLimit"] = "1000000",
                // Calls: real token minting with test keys (pure JWT, no network).
                ["LiveKit:Url"] = "wss://livekit.test",
                ["LiveKit:ApiKey"] = "testkey",
                ["LiveKit:ApiSecret"] = "testsecret0123456789testsecret0123456789"
            }));

        // API tests run without a live MinIO server: same S3 semantics in memory.
        // Production wires MinioFileStorage (covered by conditional live tests).
        builder.ConfigureServices(services => services.AddSingleton<IFileStorage, InMemoryFileStorage>());
    }

    public async Task InitializeAsync() => await CleanAsync();

    public new Task DisposeAsync() => Task.CompletedTask;

    public async Task CleanAsync()
    {
        using var scope = Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<IMongoClient>();
        var db = client.GetDatabase(TestDb);
        // deleteMany (not drops): dropping a collection also drops its indexes
        // (e.g. the messages text index), which startup does not recreate.
        foreach (var name in new[] { "users", "device_sessions", "avatars", "chats", "messages", "files", "calls", "tickets" })
        {
            await db.GetCollection<MongoDB.Bson.BsonDocument>(name)
                .DeleteManyAsync(FilterDefinition<MongoDB.Bson.BsonDocument>.Empty);
        }

        if (scope.ServiceProvider.GetRequiredService<IFileStorage>() is InMemoryFileStorage mem)
        {
            mem.Clear();
        }
    }
}

[CollectionDefinition("auth", DisableParallelization = true)]
public sealed class AuthCollection : ICollectionFixture<MessengerFactory>;

public sealed record AuthResponse(
    string AccessToken, string RefreshToken, string SessionId, UserResponse User);

public sealed record UserResponse(
    string Id, string Username, string DisplayName, string[] Roles, bool IsActive, string? AvatarUrl);

public sealed record SessionResponse(string Id, bool IsCurrent, bool IsRevoked);
