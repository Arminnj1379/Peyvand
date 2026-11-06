namespace Messenger.Domain.Chats;

/// <summary>Full stored state of a <see cref="Chat"/> for rehydration. Infrastructure use only.</summary>
public sealed record ChatSnapshot(
    string Id,
    ChatType Type,
    string? Title,
    string? Description,
    string OwnerId,
    IReadOnlyList<ChatMember> Members,
    int MemberCount,
    IReadOnlyList<string> PinnedMessageIds,
    string? LastMessageId,
    DateTime? LastMessageAt,
    string? LastMessagePreview,
    long SeqCounter,
    bool IsDeleted,
    string CreatedBy,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool IsPublic = false,
    string? Username = null,
    string? InviteCode = null,
    string? AvatarObjectKey = null,
    string? AvatarUrl = null,
    bool MembersCanInvite = false,
    string? DmKey = null);
