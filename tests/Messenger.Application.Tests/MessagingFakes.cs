using Messenger.Application.Abstractions;
using Messenger.Domain.Chats;
using Messenger.Domain.Files;
using Messenger.Domain.Messages;

namespace Messenger.Application.Tests;

/// <summary>In-memory chat/message ports for handler tests. No Mongo.</summary>
internal sealed class FakeChatStore : IChatStore
{
    private readonly Dictionary<string, Chat> _chats = [];

    /// <summary>
    /// Simulates losing the DM insert race: stores the concurrent winner, then
    /// throws, so the handler's reload path resolves it (mirrors the unique
    /// pair-key collision: the loser fails precisely because the winner's
    /// document is already committed and visible).
    /// </summary>
    public bool SimulateDmRaceOnAdd { get; set; }

    public void Seed(Chat chat) => _chats[chat.Id] = chat;

    public Task<Chat?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_chats.TryGetValue(id, out var c) && !c.IsDeleted ? c : null);

    public Task<Chat?> FindDmAsync(string userA, string userB, CancellationToken ct) =>
        Task.FromResult(_chats.Values.FirstOrDefault(c =>
            !c.IsDeleted && c.Type == ChatType.Dm && c.MemberCount == 2 &&
            c.IsMember(userA) && c.IsMember(userB)));

    public Task<Chat?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct) =>
        Task.FromResult(_chats.Values.FirstOrDefault(c =>
            !c.IsDeleted && c.InviteCode == inviteCode.Trim()));

    public Task<Chat?> GetByUsernameAsync(string username, CancellationToken ct) =>
        Task.FromResult(_chats.Values.FirstOrDefault(c =>
            !c.IsDeleted && c.Username == username.Trim().ToLowerInvariant()));

    public Task<IReadOnlyList<Chat>> ListForUserAsync(string userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Chat>>(_chats.Values
            .Where(c => !c.IsDeleted && c.IsMember(userId))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Take(limit)
            .ToList());

    public Task<IReadOnlyList<string>> ListIdsForUserAsync(string userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<string>>(_chats.Values
            .Where(c => !c.IsDeleted && c.IsMember(userId))
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Take(limit)
            .Select(c => c.Id)
            .ToList());

    public Task<bool> IsMemberAsync(string chatId, string userId, CancellationToken ct) =>
        Task.FromResult(_chats.TryGetValue(chatId, out var c) && !c.IsDeleted && c.IsMember(userId));

    public Task<bool> SharesChatAsync(string userA, string userB, CancellationToken ct) =>
        Task.FromResult(_chats.Values.Any(c => !c.IsDeleted && c.IsMember(userA) && c.IsMember(userB)));

    public Task AddAsync(Chat chat, CancellationToken ct)
    {
        if (SimulateDmRaceOnAdd && chat.Type == ChatType.Dm)
        {
            var members = chat.Members.Select(m => m.UserId).ToList();
            var winner = Chat.CreateDm("race-winner", members[0], members[1], DateTime.UtcNow).Value!;
            _chats[winner.Id] = winner;
            throw new DuplicateChatException();
        }

        _chats[chat.Id] = chat;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Chat chat, CancellationToken ct)
    {
        _chats[chat.Id] = chat;
        return Task.CompletedTask;
    }

    public Task<long> AllocateSeqAsync(string chatId, DateTime now, CancellationToken ct)
    {
        // Mirrors MongoChatStore: the counter moves atomically; the preview,
        // watermark, and unread bookkeeping land in ApplyNewMessageAsync.
        // Sequential handler flow (allocate → insert → apply) never overlaps,
        // so a pure peek is faithful here.
        var chat = _chats[chatId];
        return Task.FromResult(chat.SeqCounter + 1);
    }

    public Task ApplyNewMessageAsync(
        string chatId, long seq, string messageId, string preview, string senderId, DateTime at,
        CancellationToken ct)
    {
        _chats[chatId].ApplyNewMessage(seq, messageId, preview, senderId, at);
        return Task.CompletedTask;
    }

    public Task<bool> MarkReadAsync(string chatId, string userId, long seq, DateTime at, CancellationToken ct)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(chat.MarkRead(userId, seq, at));
    }

    public Task<bool> MarkDeliveredAsync(string chatId, string userId, long seq, DateTime at, CancellationToken ct)
    {
        if (!_chats.TryGetValue(chatId, out var chat))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(chat.MarkDelivered(userId, seq, at));
    }
}

internal sealed class FakeMessageStore : IMessageStore
{
    private readonly Dictionary<string, Message> _messages = [];

    public Task<Message?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_messages.TryGetValue(id, out var m) ? m : null);

    public Task<Message?> GetByClientKeyAsync(string senderId, string clientMessageId, CancellationToken ct) =>
        Task.FromResult(_messages.Values.FirstOrDefault(m =>
            m.SenderId == senderId && m.ClientMessageId == clientMessageId));

    public Task<Message?> GetByChatAndSeqAsync(string chatId, long seq, CancellationToken ct) =>
        Task.FromResult(_messages.Values.FirstOrDefault(m => m.ChatId == chatId && m.Seq == seq));

    public Task AddAsync(Message message, CancellationToken ct)
    {
        if (_messages.Values.Any(m =>
                m.SenderId == message.SenderId && m.ClientMessageId == message.ClientMessageId))
        {
            throw new DuplicateMessageException();
        }

        _messages[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Message message, CancellationToken ct)
    {
        _messages[message.Id] = message;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Message>> ListHistoryAsync(
        string chatId, long? beforeSeq, int limitPlusOne, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Message>>(_messages.Values
            .Where(m => m.ChatId == chatId && (!beforeSeq.HasValue || m.Seq < beforeSeq.Value))
            .OrderByDescending(m => m.Seq)
            .Take(limitPlusOne)
            .ToList());

    public Task<IReadOnlyList<Message>> ListAfterAsync(string chatId, long afterSeq, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Message>>(_messages.Values
            .Where(m => m.ChatId == chatId && m.Seq > afterSeq)
            .OrderBy(m => m.Seq)
            .Take(limit)
            .ToList());

    public Task<IReadOnlyList<Message>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Message>>(_messages.Values
            .Where(m => ids.Contains(m.Id))
            .ToList());

    public Task<IReadOnlyList<Message>> ListRepliesAsync(
        string chatId, string parentMessageId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Message>>(_messages.Values
            .Where(m => m.ChatId == chatId && m.ReplyToMessageId == parentMessageId)
            .OrderBy(m => m.Seq)
            .Take(limit)
            .ToList());

    public Task<IReadOnlyList<Message>> SearchAsync(
        IReadOnlyList<string> chatIds, string query, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Message>>(_messages.Values
            .Where(m => !m.IsDeleted && chatIds.Contains(m.ChatId) &&
                m.Body.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToList());

    public Task IncrementReplyCountAsync(string messageId, CancellationToken ct)
    {
        if (_messages.TryGetValue(messageId, out var m))
        {
            m.BumpReplyCount();
        }

        return Task.CompletedTask;
    }
}

internal sealed class FakeFileStore : IFileStore
{
    private readonly Dictionary<string, ChatFile> _files = [];

    public Task<ChatFile?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(id, out var f) ? f : null);

    public Task<IReadOnlyList<ChatFile>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<ChatFile>>(_files.Values
            .Where(f => ids.Contains(f.Id))
            .ToList());

    public Task AddAsync(ChatFile file, CancellationToken ct)
    {
        _files[file.Id] = file;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(ChatFile file, CancellationToken ct)
    {
        _files[file.Id] = file;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        _files.Remove(id);
        return Task.CompletedTask;
    }
}

internal sealed class FakeFileStorage : IFileStorage
{
    public readonly Dictionary<string, (byte[] Bytes, string ContentType)> Objects = [];
    public bool FailNext { get; set; }

    public Task PutAsync(string objectKey, string contentType, byte[] bytes, CancellationToken ct)
    {
        if (FailNext)
        {
            throw new InvalidOperationException("storage down");
        }

        Objects[objectKey] = (bytes, contentType);
        return Task.CompletedTask;
    }

    public Task<(byte[] Bytes, string ContentType, long SizeBytes)?> GetAsync(string objectKey, CancellationToken ct)
    {
        if (FailNext)
        {
            throw new InvalidOperationException("storage down");
        }

        return Task.FromResult(Objects.TryGetValue(objectKey, out var o)
            ? ((byte[], string, long)?)(o.Bytes, o.ContentType, o.Bytes.Length)
            : null);
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        Objects.Remove(objectKey);
        return Task.CompletedTask;
    }

    public Task EnsureBucketAsync(CancellationToken ct) => Task.CompletedTask;
}

internal sealed class FakeFileScanner : IFileScanner
{
    public ScanVerdict Verdict { get; set; } = ScanVerdict.Clean;

    public Task<ScanVerdict> ScanAsync(byte[] bytes, string contentType, CancellationToken ct) =>
        Task.FromResult(Verdict);
}
