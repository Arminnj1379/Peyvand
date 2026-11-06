namespace Messenger.Domain.Chats;

/// <summary>Embedded membership. Read state is a watermark, not per-message rows.</summary>
public sealed record ChatMember(
    string UserId,
    ChatRole Role,
    DateTime JoinedAt,
    long LastReadSeq,
    DateTime? LastReadAt,
    int UnreadCount,
    bool IsMuted,
    bool IsArchived,
    long LastDeliveredSeq = 0);
