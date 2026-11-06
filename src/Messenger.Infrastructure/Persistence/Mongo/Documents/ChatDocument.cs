using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Messenger.Infrastructure.Persistence.Mongo.Documents;

/// <summary>Embedded membership (collection <c>chats</c>, array <c>members</c>).</summary>
public sealed class ChatMemberDocument
{
    [BsonElement("userId"), BsonRepresentation(BsonType.ObjectId)]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("role"), BsonRepresentation(BsonType.String)]
    public Domain.Chats.ChatRole Role { get; set; }

    [BsonElement("joinedAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime JoinedAt { get; set; }

    [BsonElement("lastReadSeq")]
    public long LastReadSeq { get; set; }

    [BsonElement("lastReadAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastReadAt { get; set; }

    [BsonElement("unreadCount")]
    public int UnreadCount { get; set; }

    [BsonElement("isMuted")]
    public bool IsMuted { get; set; }

    [BsonElement("isArchived")]
    public bool IsArchived { get; set; }

    [BsonElement("lastDeliveredSeq")]
    public long LastDeliveredSeq { get; set; }
}

/// <summary>Mongo shape of the Chat aggregate (collection <c>chats</c>).</summary>
public sealed class ChatDocument
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = string.Empty;

    [BsonElement("type"), BsonRepresentation(BsonType.String)]
    public Domain.Chats.ChatType Type { get; set; }

    [BsonElement("title"), BsonIgnoreIfNull]
    public string? Title { get; set; }

    [BsonElement("description"), BsonIgnoreIfNull]
    public string? Description { get; set; }

    [BsonElement("ownerId"), BsonRepresentation(BsonType.ObjectId)]
    public string OwnerId { get; set; } = string.Empty;

    [BsonElement("members")]
    public List<ChatMemberDocument> Members { get; set; } = [];

    [BsonElement("memberCount")]
    public int MemberCount { get; set; }

    [BsonElement("pinnedMessageIds")]
    public List<string> PinnedMessageIds { get; set; } = [];

    [BsonElement("lastMessageId"), BsonIgnoreIfNull, BsonRepresentation(BsonType.ObjectId)]
    public string? LastMessageId { get; set; }

    [BsonElement("lastMessageAt"), BsonIgnoreIfNull, BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? LastMessageAt { get; set; }

    [BsonElement("lastMessagePreview"), BsonIgnoreIfNull]
    public string? LastMessagePreview { get; set; }

    [BsonElement("seqCounter")]
    public long SeqCounter { get; set; }

    [BsonElement("isDeleted")]
    public bool IsDeleted { get; set; }

    [BsonElement("isPublic")]
    public bool IsPublic { get; set; }

    [BsonElement("username"), BsonIgnoreIfNull]
    public string? Username { get; set; }

    [BsonElement("inviteCode"), BsonIgnoreIfNull]
    public string? InviteCode { get; set; }

    [BsonElement("avatarObjectKey"), BsonIgnoreIfNull]
    public string? AvatarObjectKey { get; set; }

    [BsonElement("avatarUrl"), BsonIgnoreIfNull]
    public string? AvatarUrl { get; set; }

    [BsonElement("membersCanInvite")]
    public bool MembersCanInvite { get; set; }

    [BsonElement("dmKey"), BsonIgnoreIfNull]
    public string? DmKey { get; set; }

    [BsonElement("createdBy"), BsonRepresentation(BsonType.ObjectId)]
    public string CreatedBy { get; set; } = string.Empty;

    [BsonElement("createdAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; }

    [BsonElement("updatedAt"), BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; }

    [BsonElement("schemaV")]
    public int SchemaVersion { get; set; } = 1;
}
