using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>Mongo shape of the User aggregate (collection <c>users</c>).</summary>
public sealed class UserDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    [BsonElement("phone"), BsonIgnoreIfNull]
    public string? Phone { get; set; }

    [BsonElement("phoneVerified")]
    public bool PhoneVerified { get; set; }

    [BsonElement("userType"), BsonRepresentation(BsonType.String)]
    public Domain.Users.UserType UserType { get; set; }

    [BsonElement("roles")]
    public List<string> Roles { get; set; } = [];

    [BsonElement("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [BsonElement("bio")]
    public string Bio { get; set; } = string.Empty;

    [BsonElement("theme")]
    public string Theme { get; set; } = "system";

    [BsonElement("notificationsEnabled")]
    public bool NotificationsEnabled { get; set; } = true;

    [BsonElement("avatarObjectKey"), BsonIgnoreIfNull]
    public string? AvatarObjectKey { get; set; }

    [BsonElement("avatarUrl"), BsonIgnoreIfNull]
    public string? AvatarUrl { get; set; }

    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;

    [BsonElement("blockedUserIds")]
    public List<string> BlockedUserIds { get; set; } = [];

    [BsonElement("lastSeenAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastSeenAt { get; set; }

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
