using Messenger.Domain.Chats;

namespace Messenger.Application.Abstractions;

/// <summary>Conversation store. One method per use-case need — no generics.</summary>
public interface IChatStore
{
    Task<Chat?> GetByIdAsync(string id, CancellationToken ct);
    Task<Chat?> FindDmAsync(string userA, string userB, CancellationToken ct);
    Task<Chat?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct);
    Task<Chat?> GetByUsernameAsync(string username, CancellationToken ct);
    Task<IReadOnlyList<Chat>> ListForUserAsync(string userId, int limit, CancellationToken ct);
    /// <summary>
    /// Id-only variant of <see cref="ListForUserAsync"/> (projection, no member
    /// arrays) for connection setup and other id-only paths.
    /// </summary>
    Task<IReadOnlyList<string>> ListIdsForUserAsync(string userId, int limit, CancellationToken ct);
    /// <summary>
    /// Lightweight membership probe (existence check, no document payload) for
    /// hot paths that already hold the entity or need none (hub invokes).
    /// </summary>
    Task<bool> IsMemberAsync(string chatId, string userId, CancellationToken ct);
    /// <summary>
    /// Whether both users share at least one live conversation. Drives
    /// customer-to-customer profile/avatar visibility.
    /// </summary>
    Task<bool> SharesChatAsync(string userA, string userB, CancellationToken ct);
    Task AddAsync(Chat chat, CancellationToken ct);
    Task UpdateAsync(Chat chat, CancellationToken ct);

    /// <summary>Atomic in-doc <c>$inc</c> of the per-chat sequence (ADR: seq allocation).</summary>
    Task<long> AllocateSeqAsync(string chatId, DateTime now, CancellationToken ct);

    /// <summary>
    /// Atomic post-send update: last-message fields, sender watermark advance,
    /// unread increment for every other member. Runs after the message insert.
    /// </summary>
    Task ApplyNewMessageAsync(
        string chatId, long seq, string messageId, string preview, string senderId, DateTime at,
        CancellationToken ct);

    /// <summary>Compare-and-set watermark advance. Returns true when advanced.</summary>
    Task<bool> MarkReadAsync(string chatId, string userId, long seq, DateTime at, CancellationToken ct);

    /// <summary>Monotonic delivery-watermark advance. Returns true when advanced.</summary>
    Task<bool> MarkDeliveredAsync(string chatId, string userId, long seq, DateTime at, CancellationToken ct);
}
