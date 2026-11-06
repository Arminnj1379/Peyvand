using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Domain.Messages;

/// <summary>
/// Chat message aggregate. One doc per message in <c>messages</c>; history is
/// never embedded in the chat doc. Soft-deleted bodies are wiped.
/// </summary>
public sealed class Message : Entity
{
    public const int MaxBodyLength = 4000;
    public const int MaxReactions = 100;
    public const int MaxDistinctEmojis = 20;

    private readonly List<MessageReaction> _reactions = [];

    private Message(
        string id,
        string chatId,
        long seq,
        string clientMessageId,
        string? senderId,
        MessageKind kind,
        string body,
        string? replyToMessageId,
        ForwardInfo? forwardFrom,
        DateTime now,
        string? fileId = null) : base(id)
    {
        ChatId = chatId;
        Seq = seq;
        ClientMessageId = clientMessageId;
        SenderId = senderId;
        Kind = kind;
        Body = body;
        ReplyToMessageId = replyToMessageId;
        ForwardFrom = forwardFrom;
        FileId = fileId;
        CreatedAt = now;
    }

    public string ChatId { get; }
    public long Seq { get; }
    public string ClientMessageId { get; }
    public string? SenderId { get; }
    public MessageKind Kind { get; }
    public string Body { get; private set; }
    public string? ReplyToMessageId { get; }
    public ForwardInfo? ForwardFrom { get; }
    public bool IsEdited { get; private set; }
    public DateTime? EditedAt { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAt { get; private set; }
    public string? DeletedBy { get; private set; }
    public DateTime CreatedAt { get; private set; }

    /// <summary>Attachment reference (collection <c>files</c>). Null for text-only messages.</summary>
    public string? FileId { get; }
    public bool HasAttachment => FileId is not null;
    public IReadOnlyList<MessageReaction> Reactions => _reactions.AsReadOnly();

    /// <summary>Denormalized count of direct replies (thread children). Incremented on reply send.</summary>
    public int ReplyCount { get; private set; }

    public static Result<Message> Create(
        string id,
        string chatId,
        long seq,
        string clientMessageId,
        string? senderId,
        MessageKind kind,
        string body,
        string? replyToMessageId,
        ForwardInfo? forwardFrom,
        DateTime now,
        string? fileId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        if (string.IsNullOrWhiteSpace(clientMessageId) || clientMessageId.Length > 64)
        {
            return Result<Message>.Fail(MessageErrors.InvalidClientMessageId);
        }

        // Captions are optional on attachment messages; text messages still need a body.
        var trimmedBody = body.Trim();
        if (fileId is null && !IsValidBody(body))
        {
            return Result<Message>.Fail(MessageErrors.InvalidBody);
        }

        if (fileId is not null && (fileId.Length > 64 || trimmedBody.Length > MaxBodyLength))
        {
            return Result<Message>.Fail(MessageErrors.InvalidBody);
        }

        if (kind == MessageKind.Reply && string.IsNullOrWhiteSpace(replyToMessageId))
        {
            return Result<Message>.Fail(MessageErrors.ReplyTargetNotFound);
        }

        if (kind == MessageKind.Forward && forwardFrom is null)
        {
            return Result<Message>.Fail(MessageErrors.ForwardSourceNotFound);
        }

        var message = new Message(
            id, chatId, seq, clientMessageId.Trim(), senderId, kind,
            trimmedBody, replyToMessageId, forwardFrom, now, fileId);
        message.Raise(new MessageSent(chatId, id, seq, senderId, now));
        return Result<Message>.Ok(message);
    }

    public Result Edit(string body, DateTime now)
    {
        if (IsDeleted)
        {
            return Result.Fail(MessageErrors.AlreadyDeleted);
        }

        // Captions may be cleared entirely when an attachment carries the message.
        var trimmedBody = body.Trim();
        if (FileId is null && !IsValidBody(body))
        {
            return Result.Fail(MessageErrors.InvalidBody);
        }

        if (trimmedBody.Length > MaxBodyLength)
        {
            return Result.Fail(MessageErrors.InvalidBody);
        }

        Body = trimmedBody;
        IsEdited = true;
        EditedAt = now;
        Raise(new MessageEdited(ChatId, Id, now));
        return Result.Ok();
    }

    public Result Delete(string deletedBy, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deletedBy);
        if (IsDeleted)
        {
            return Result.Fail(MessageErrors.AlreadyDeleted);
        }

        IsDeleted = true;
        Body = string.Empty;
        DeletedAt = now;
        DeletedBy = deletedBy;
        _reactions.Clear();
        Raise(new MessageDeleted(ChatId, Id, deletedBy, now));
        return Result.Ok();
    }

    /// <summary>
    /// Toggles <paramref name="userId"/>'s reaction. Returns true when added,
    /// false when removed. Reactions on deleted messages are rejected.
    /// </summary>
    public Result<bool> ToggleReaction(string userId, string emoji, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (IsDeleted)
        {
            return Result<bool>.Fail(MessageErrors.CannotReact);
        }

        if (!IsValidEmoji(emoji))
        {
            return Result<bool>.Fail(MessageErrors.ReactionInvalid);
        }

        var normalized = emoji.Trim();
        var existing = _reactions.FirstOrDefault(r => r.UserId == userId && r.Emoji == normalized);
        if (existing is not null)
        {
            _reactions.Remove(existing);
            Raise(new MessageReacted(ChatId, Id, userId, normalized, false, now));
            return Result<bool>.Ok(false);
        }

        if (_reactions.Count >= MaxReactions)
        {
            return Result<bool>.Fail(MessageErrors.TooManyReactions);
        }

        if (_reactions.Select(r => r.Emoji).Distinct().Count() >= MaxDistinctEmojis &&
            !_reactions.Any(r => r.Emoji == normalized))
        {
            return Result<bool>.Fail(MessageErrors.TooManyReactions);
        }

        _reactions.Add(new MessageReaction(normalized, userId, now));
        Raise(new MessageReacted(ChatId, Id, userId, normalized, true, now));
        return Result<bool>.Ok(true);
    }

    public void BumpReplyCount() => ReplyCount++;

    /// <summary>
    /// Persistence rehydration. Bypasses <see cref="Create"/> (no events,
    /// no re-validation of already-stored state). Infrastructure use only.
    /// </summary>
    public static Message Rehydrate(MessageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var message = new Message(
            snapshot.Id, snapshot.ChatId, snapshot.Seq, snapshot.ClientMessageId,
            snapshot.SenderId, snapshot.Kind, snapshot.Body, snapshot.ReplyToMessageId,
            snapshot.ForwardFrom, snapshot.CreatedAt, snapshot.FileId);
        message.IsEdited = snapshot.IsEdited;
        message.EditedAt = snapshot.EditedAt;
        message.IsDeleted = snapshot.IsDeleted;
        message.DeletedAt = snapshot.DeletedAt;
        message.DeletedBy = snapshot.DeletedBy;
        if (snapshot.Reactions is not null)
        {
            message._reactions.AddRange(snapshot.Reactions);
        }

        message.ReplyCount = snapshot.ReplyCount;
        return message;
    }

    public static bool IsValidEmoji(string emoji) =>
        !string.IsNullOrWhiteSpace(emoji) && emoji.Trim().Length is >= 1 and <= 16;

    public static bool IsValidBody(string body) =>
        !string.IsNullOrWhiteSpace(body) && body.Trim().Length is >= 1 and <= MaxBodyLength;

    public static string PreviewOf(string body, int max = 140)
    {
        var singleLine = body.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return singleLine.Length <= max ? singleLine : singleLine[..max].TrimEnd() + "…";
    }
}
