using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>Provenance of a forwarded copy (embedded in <c>messages</c>).</summary>
public sealed class ForwardInfoDocument
{
    [BsonElement("chatId"), BsonRepresentation(BsonType.ObjectId)]
    public string ChatId { get; set; } = string.Empty;

    [BsonElement("messageId"), BsonRepresentation(BsonType.ObjectId)]
    public string MessageId { get; set; } = string.Empty;

    [BsonElement("senderId")]
    public string SenderId { get; set; } = string.Empty;

    [BsonElement("sentAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime SentAt { get; set; }
}

/// <summary>Mongo shape of one emoji reaction (embedded in <c>messages</c>).</summary>
public sealed class MessageReactionDocument
{
    [BsonElement("emoji")]
    public string Emoji { get; set; } = string.Empty;

    [BsonElement("userId"), BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("at"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime At { get; set; }
}

/// <summary>Mongo shape of the Message aggregate (collection <c>messages</c>).</summary>
public sealed class MessageDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("chatId"), BsonRepresentation(BsonType.ObjectId)]
    public string ChatId { get; set; } = string.Empty;

    [BsonElement("seq")]
    public long Seq { get; set; }

    [BsonElement("clientMessageId")]
    public string ClientMessageId { get; set; } = string.Empty;

    [BsonElement("senderId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? SenderId { get; set; }

    [BsonElement("kind"), BsonRepresentation(BsonType.String)]
    public Domain.Messages.MessageKind Kind { get; set; }

    [BsonElement("body")]
    public string Body { get; set; } = string.Empty;

    [BsonElement("replyToMessageId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? ReplyToMessageId { get; set; }

    [BsonElement("forwardFrom"), BsonIgnoreIfNull]
    public ForwardInfoDocument? ForwardFrom { get; set; }

    [BsonElement("fileId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? FileId { get; set; }

    [BsonElement("isEdited")]
    public bool IsEdited { get; set; }

    [BsonElement("editedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EditedAt { get; set; }

    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; }

    [BsonElement("deletedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? DeletedAt { get; set; }

    [BsonElement("deletedBy"), BsonIgnoreIfNull]
    public string? DeletedBy { get; set; }

    [BsonElement("reactions")]
    public List<MessageReactionDocument> Reactions { get; set; } = [];

    [BsonElement("replyCount")]
    public int ReplyCount { get; set; }

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
