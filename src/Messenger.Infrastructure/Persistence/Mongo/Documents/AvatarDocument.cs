using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>
/// Avatar bytes (collection <c>avatars</c>, one doc per owner).
/// The owner is a user id, or <c>chat-{chatId}</c> for group/channel avatars.
/// Mongo-backed until the Media/MinIO slice; clients never see this shape.
/// </summary>
public sealed class AvatarDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Owner namespace (<c>{userId}</c> or <c>chat-{chatId}</c>). Rows written
    /// before this field existed keyed the owner id as <c>_id</c> instead.
    /// </summary>
    [BsonElement("ownerKey")]
    public string OwnerKey { get; set; } = string.Empty;

    [BsonElement("objectKey")]
    public string ObjectKey { get; set; } = string.Empty;

    [BsonElement("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [BsonElement("bytes")]
    public byte[] Bytes { get; set; } = [];

    [BsonElement("updatedAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
