using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Api.Tests;

/// <summary>
/// Verifies startup index bootstrap: every serving index exists with the
/// expected keys. Guards against silent index drift (typo'd names, dropped
/// definitions) that would turn hot paths into collection scans.
/// </summary>
[Collection("auth")]
public sealed class MongoIndexesTests(MessengerFactory factory)
{
    private static void AssertIndex(
        IMongoDatabase db, string collection, string name, string expectedKeyJson)
    {
        var indexes = db.GetCollection<BsonDocument>(collection)
            .Indexes.List().ToList();
        var match = indexes.FirstOrDefault(i =>
            i.GetValue("name", "").AsString == name);
        Assert.True(match is not null, $"Index {name} missing on {collection}.");

        // Whitespace-insensitive key comparison (driver JSON spacing varies).
        static string Norm(string s) => new(s.Where(c => !char.IsWhiteSpace(c)).ToArray());
        Assert.Equal(
            Norm(expectedKeyJson),
            Norm(match!.GetValue("key").ToJson()));
    }

    [Fact]
    public async Task Startup_creates_all_serving_indexes()
    {
        await factory.CleanAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IMongoClient>()
            .GetDatabase(MessengerFactory.TestDb);

        // Hot paths: history pages, idempotent resend, thread pages, search.
        AssertIndex(db, "messages", "ix_history", """{ "chatId" : 1, "seq" : -1 }""");
        AssertIndex(db, "messages", "ux_sender_client", """{ "senderId" : 1, "clientMessageId" : 1 }""");
        AssertIndex(db, "messages", "ix_thread", """{ "chatId" : 1, "replyToMessageId" : 1, "seq" : 1 }""");
        AssertIndex(db, "messages", "txt_body", """{ "_fts" : "text", "_ftsx" : 1 }""");

        // Chat list, lookups, DM-pair and live-call guards.
        AssertIndex(db, "chats", "ix_member_list", """{ "members.userId" : 1, "lastMessageAt" : -1 }""");
        AssertIndex(db, "chats", "ux_channel_username", """{ "username" : 1 }""");
        AssertIndex(db, "chats", "ux_invite_code", """{ "inviteCode" : 1 }""");
        AssertIndex(db, "chats", "ux_dm_key", """{ "dmKey" : 1 }""");
        AssertIndex(db, "calls", "ix_chat_created", """{ "chatId" : 1, "createdAt" : -1 }""");
        AssertIndex(db, "calls", "ux_live_call", """{ "chatId" : 1 }""");

        // Identity, sessions, avatars.
        AssertIndex(db, "users", "ux_username", """{ "username" : 1 }""");
        AssertIndex(db, "users", "ux_phone", """{ "phone" : 1 }""");
        AssertIndex(db, "device_sessions", "ux_refresh_hash", """{ "refreshTokenHash" : 1 }""");
        AssertIndex(db, "device_sessions", "ttl_expires", """{ "expiresAt" : 1 }""");
        AssertIndex(db, "avatars", "ux_object_key", """{ "objectKey" : 1 }""");
    }
}
