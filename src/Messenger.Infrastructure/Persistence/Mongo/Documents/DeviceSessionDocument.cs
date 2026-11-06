using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>Mongo shape of the DeviceSession aggregate (collection <c>device_sessions</c>).</summary>
public sealed class DeviceSessionDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("userId"), BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("refreshTokenHash")]
    public string RefreshTokenHash { get; set; } = string.Empty;

    [BsonElement("retiredTokenHashes")]
    public List<string> RetiredTokenHashes { get; set; } = [];

    [BsonElement("deviceLabel"), BsonIgnoreIfNull]
    public string? DeviceLabel { get; set; }

    [BsonElement("ipAddress"), BsonIgnoreIfNull]
    public string? IpAddress { get; set; }

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("lastUsedAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime LastUsedAt { get; set; }

    [BsonElement("expiresAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime ExpiresAt { get; set; }

    [BsonElement("revokedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? RevokedAt { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
