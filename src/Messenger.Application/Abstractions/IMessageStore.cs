using Messenger.Domain.Messages;

namespace Messenger.Application.Abstractions;

/// <summary>Message store (collection <c>messages</c>). No history inside chat docs.</summary>
public interface IMessageStore
{
    Task<Message?> GetByIdAsync(string id, CancellationToken ct);
    Task<Message?> GetByClientKeyAsync(string senderId, string clientMessageId, CancellationToken ct);
    Task<Message?> GetByChatAndSeqAsync(string chatId, long seq, CancellationToken ct);
    Task AddAsync(Message message, CancellationToken ct);
    Task UpdateAsync(Message message, CancellationToken ct);

    /// <summary>
    /// Cursor page: messages with <c>seq &lt; beforeSeq</c> (or latest when null),
    /// newest-first, up to <paramref name="limit"/>+1 rows (extra signals <c>hasMore</c>).
    /// </summary>
    Task<IReadOnlyList<Message>> ListHistoryAsync(string chatId, long? beforeSeq, int limitPlusOne, CancellationToken ct);

    /// <summary>Gap-fill for reconnects: <c>seq &gt; afterSeq</c>, oldest-first.</summary>
    Task<IReadOnlyList<Message>> ListAfterAsync(string chatId, long afterSeq, int limit, CancellationToken ct);

    Task<IReadOnlyList<Message>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct);

    /// <summary>Direct replies to a message (single thread level), oldest-first.</summary>
    Task<IReadOnlyList<Message>> ListRepliesAsync(string chatId, string parentMessageId, int limit, CancellationToken ct);

    /// <summary>
    /// Full-text search scoped to the caller's chats (uses the <c>txt_body</c>
    /// index; deleted messages are excluded by the handler, not the store).
    /// </summary>
    Task<IReadOnlyList<Message>> SearchAsync(
        IReadOnlyList<string> chatIds, string query, int limit, CancellationToken ct);

    /// <summary>Atomic <c>$inc</c> of the denormalized reply counter.</summary>
    Task IncrementReplyCountAsync(string messageId, CancellationToken ct);
}
