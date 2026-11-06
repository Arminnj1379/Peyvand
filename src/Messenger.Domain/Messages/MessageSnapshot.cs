namespace Messenger.Domain.Messages;

/// <summary>Full stored state of a <see cref="Message"/> for rehydration. Infrastructure use only.</summary>
public sealed record MessageSnapshot(
    string Id,
    string ChatId,
    long Seq,
    string ClientMessageId,
    string? SenderId,
    MessageKind Kind,
    string Body,
    string? ReplyToMessageId,
    ForwardInfo? ForwardFrom,
    bool IsEdited,
    DateTime? EditedAt,
    bool IsDeleted,
    DateTime? DeletedAt,
    string? DeletedBy,
    DateTime CreatedAt,
    IReadOnlyList<MessageReaction>? Reactions = null,
    int ReplyCount = 0,
    string? FileId = null);
