using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>Mongo shape of the SupportTicket aggregate (collection <c>tickets</c>).</summary>
public sealed class TicketDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("customerId"), BsonRepresentation(BsonType.ObjectId)]
    public string CustomerId { get; set; } = string.Empty;

    [BsonElement("subject")]
    public string Subject { get; set; } = string.Empty;

    [BsonElement("description")]
    public string Description { get; set; } = string.Empty;

    [BsonElement("status"), BsonRepresentation(BsonType.String)]
    public Domain.Support.TicketStatus Status { get; set; }

    [BsonElement("assigneeId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? AssigneeId { get; set; }

    [BsonElement("chatId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? ChatId { get; set; }

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("closedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ClosedAt { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
