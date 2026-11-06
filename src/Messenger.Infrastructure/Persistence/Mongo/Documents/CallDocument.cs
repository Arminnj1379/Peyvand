using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>Mongo shape of one call participant (embedded in <c>calls</c>).</summary>
public sealed class CallParticipantDocument
{
    [BsonElement("userId"), BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("status"), BsonRepresentation(BsonType.String)]
    public Domain.Calls.CallParticipantStatus Status { get; set; }

    [BsonElement("isJoined")]
    public bool IsJoined { get; set; }

    [BsonElement("invitedAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime InvitedAt { get; set; }

    [BsonElement("joinedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? JoinedAt { get; set; }
}

/// <summary>
/// Mongo shape of the Call aggregate (collection <c>calls</c>).
/// Signaling state only — media flows through LiveKit.
/// </summary>
public sealed class CallDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("chatId"), BsonRepresentation(BsonType.ObjectId)]
    public string ChatId { get; set; } = string.Empty;

    [BsonElement("initiatorId"), BsonRepresentation(BsonType.ObjectId)]
    public string InitiatorId { get; set; } = string.Empty;

    [BsonElement("mediaType"), BsonRepresentation(BsonType.String)]
    public Domain.Calls.CallMediaType MediaType { get; set; }

    [BsonElement("status"), BsonRepresentation(BsonType.String)]
    public Domain.Calls.CallStatus Status { get; set; }

    [BsonElement("roomName")]
    public string RoomName { get; set; } = string.Empty;

    [BsonElement("participants")]
    public List<CallParticipantDocument> Participants { get; set; } = [];

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("startedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? StartedAt { get; set; }

    [BsonElement("endedAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? EndedAt { get; set; }

    [BsonElement("endedBy"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? EndedBy { get; set; }

    [BsonElement("endReason"), BsonIgnoreIfNull, BsonRepresentation(BsonType.String)]
    public Domain.Calls.CallEndReason? EndReason { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
