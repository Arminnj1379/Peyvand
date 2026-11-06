using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>
/// Mongo shape of the ChatFile aggregate (collection <c>files</c>).
/// Metadata only — binaries live in S3-compatible object storage.
/// </summary>
public sealed class FileDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("ownerId"), BsonRepresentation(BsonType.ObjectId)]
    public string OwnerId { get; set; } = string.Empty;

    [BsonElement("chatId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? ChatId { get; set; }

    [BsonElement("messageId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? MessageId { get; set; }

    [BsonElement("fileName")]
    public string FileName { get; set; } = string.Empty;

    [BsonElement("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [BsonElement("sizeBytes")]
    public long SizeBytes { get; set; }

    [BsonElement("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [BsonElement("kind"), BsonRepresentation(BsonType.String)]
    public Domain.Files.FileKind Kind { get; set; }

    [BsonElement("status"), BsonRepresentation(BsonType.String)]
    public Domain.Files.FileStatus Status { get; set; }

    [BsonElement("objectKey")]
    public string ObjectKey { get; set; } = string.Empty;

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("attachedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? AttachedAt { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
