using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Application.Services.Interfaces;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;

namespace Messenger.Application.Services.Implementations;

/// <summary>Message use cases. Logic moved verbatim from the former MediatR handlers.</summary>
public sealed class MessageService(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    IIdGenerator ids,
    IClock clock) : IMessageService
{
    public async Task<Result<MessageDto>> SendMessageAsync(
        SendMessageCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SendMessageValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var senderId = caller.UserId;

        // Idempotency fast path: the client retried (same sender + key).
        var existing = await messages.GetByClientKeyAsync(senderId, command.ClientMessageId.Trim(), ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ChatId != command.ChatId)
            {
                return Result<MessageDto>.Fail(MessageErrors.ClientIdConflict);
            }

            return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, existing, ct, senderId).ConfigureAwait(false));
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var authorized = ChatAccess.RequireCanPost(chat, senderId);
        if (authorized.IsFailure)
        {
            return Result<MessageDto>.Fail(authorized.Error);
        }

        // A block stops DM delivery in both directions (neutral error, no leak).
        // Both users arrive in one round-trip; only the block lists are read.
        if (chat!.Type == ChatType.Dm)
        {
            var peerId = chat.Members.FirstOrDefault(m => m.UserId != senderId)?.UserId;
            if (peerId is not null)
            {
                var pair = await users.ListByIdsAsync([senderId, peerId], ct).ConfigureAwait(false);
                var peer = pair.FirstOrDefault(u => u.Id == peerId);
                var self = pair.FirstOrDefault(u => u.Id == senderId);
                if ((peer?.BlockedUserIds.Contains(senderId) ?? false) ||
                    (self?.BlockedUserIds.Contains(peerId) ?? false))
                {
                    return Result<MessageDto>.Fail(ChatErrors.Blocked);
                }
            }
        }

        string? replyTo = null;
        MessageKind kind = MessageKind.Text;
        if (!string.IsNullOrWhiteSpace(command.ReplyToMessageId))
        {
            var target = await messages.GetByIdAsync(command.ReplyToMessageId, ct).ConfigureAwait(false);
            if (target is null || target.ChatId != command.ChatId || target.IsDeleted)
            {
                return Result<MessageDto>.Fail(MessageErrors.ReplyTargetNotFound);
            }

            replyTo = target.Id;
            kind = MessageKind.Reply;
        }

        // Attachments must be the sender's own ready, unattached uploads.
        Domain.Files.ChatFile? upload = null;
        if (!string.IsNullOrWhiteSpace(command.FileId))
        {
            upload = await files.GetByIdAsync(command.FileId, ct).ConfigureAwait(false);
            if (upload is null || upload.Status != Domain.Files.FileStatus.Ready)
            {
                return Result<MessageDto>.Fail(Domain.Files.FileErrors.NotFound);
            }

            if (upload.OwnerId != senderId)
            {
                return Result<MessageDto>.Fail(Domain.Files.FileErrors.NotOwner);
            }

            if (upload.IsAttached)
            {
                return Result<MessageDto>.Fail(Domain.Files.FileErrors.AlreadyAttached);
            }
        }

        var now = clock.UtcNow;
        var seq = await chats.AllocateSeqAsync(chat!.Id, now, ct).ConfigureAwait(false);
        var created = Message.Create(
            ids.NewId(), chat.Id, seq, command.ClientMessageId.Trim(),
            senderId, kind, command.Body, replyTo, null, now, upload?.Id);
        if (created.IsFailure)
        {
            return Result<MessageDto>.Fail(created.Error);
        }

        try
        {
            await messages.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateMessageException)
        {
            // Lost the insert race with our own retry: load the winner.
            var winner = await messages.GetByClientKeyAsync(senderId, command.ClientMessageId.Trim(), ct)
                .ConfigureAwait(false);
            if (winner is null)
            {
                throw;
            }

            if (winner.ChatId != command.ChatId)
            {
                return Result<MessageDto>.Fail(MessageErrors.ClientIdConflict);
            }

            return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, winner, ct, senderId).ConfigureAwait(false));
        }

        var preview = string.IsNullOrWhiteSpace(command.Body) && upload is not null
            ? Domain.Files.FileValidation.PreviewLabel(upload.Kind, upload.FileName)
            : Message.PreviewOf(command.Body);
        await chats.ApplyNewMessageAsync(
            chat.Id, seq, created.Value!.Id, preview, senderId, now, ct).ConfigureAwait(false);

        if (upload is not null)
        {
            // Link the upload to its message (single attachment target per file).
            var attached = upload.Attach(chat.Id, created.Value.Id, now);
            if (attached.IsFailure)
            {
                return Result<MessageDto>.Fail(attached.Error);
            }

            await files.UpdateAsync(upload, ct).ConfigureAwait(false);
        }

        if (replyTo is not null)
        {
            // Denormalized thread counter; best-effort (history stays correct regardless).
            await messages.IncrementReplyCountAsync(replyTo, ct).ConfigureAwait(false);
            created.Value.BumpReplyCount();
        }

        return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, created.Value, ct, senderId).ConfigureAwait(false));
    }

    public async Task<Result<HistoryPageDto>> GetHistoryAsync(
        GetHistoryQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetHistoryValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<HistoryPageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(query.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<HistoryPageDto>.Fail(member.Error);
        }

        // Store returns newest-first with one extra probe row for hasMore.
        var rows = await messages.ListHistoryAsync(chat!.Id, query.BeforeSeq, query.Limit + 1, ct)
            .ConfigureAwait(false);
        var hasMore = rows.Count > query.Limit;
        var page = rows.Take(query.Limit).ToList();
        var dtos = await MessageQueries.EnrichManyAsync(messages, files, users, page, ct, caller.UserId).ConfigureAwait(false);

        // Client renders oldest-first; cursor is the oldest seq on this page.
        var ordered = dtos.OrderBy(d => d.Seq).ToList();
        long? nextCursor = ordered.Count == 0 ? query.BeforeSeq : ordered.First().Seq;
        if (!hasMore)
        {
            nextCursor = ordered.Count == 0 ? null : ordered.First().Seq;
        }

        return Result<HistoryPageDto>.Ok(new HistoryPageDto(ordered, hasMore ? nextCursor : null, hasMore));
    }

    public async Task<Result<IReadOnlyList<MessageDto>>> SyncMessagesAsync(
        SyncMessagesQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new SyncMessagesValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(query.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(member.Error);
        }

        var rows = await messages.ListAfterAsync(chat!.Id, query.AfterSeq, query.Limit, ct)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<MessageDto>>.Ok(
            await MessageQueries.EnrichManyAsync(messages, files, users, rows, ct, caller.UserId).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<MessageDto>>> ListPinnedAsync(
        ListPinnedQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new ListPinnedValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(query.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(member.Error);
        }

        if (chat!.PinnedMessageIds.Count == 0)
        {
            return Result<IReadOnlyList<MessageDto>>.Ok([]);
        }

        var rows = await messages.ListByIdsAsync(chat.PinnedMessageIds, ct).ConfigureAwait(false);
        var byId = rows.ToDictionary(m => m.Id);
        var ordered = chat.PinnedMessageIds
            .Where(byId.ContainsKey)
            .Select(id => byId[id])
            .ToList();
        return Result<IReadOnlyList<MessageDto>>.Ok(
            await MessageQueries.EnrichManyAsync(messages, files, users, ordered, ct, caller.UserId).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<MessageDto>>> SearchMessagesAsync(
        SearchMessagesQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new SearchMessagesValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var mine = await chats.ListForUserAsync(caller.UserId, 200, ct).ConfigureAwait(false);
        var chatIds = mine.Where(c => !c.IsDeleted).Select(c => c.Id).ToList();
        if (chatIds.Count == 0)
        {
            return Result<IReadOnlyList<MessageDto>>.Ok([]);
        }

        var found = await messages.SearchAsync(chatIds, query.Query.Trim(), query.Limit, ct)
            .ConfigureAwait(false);
        var visible = found.Where(m => !m.IsDeleted).ToList();
        var dtos = await MessageQueries.EnrichManyAsync(messages, files, users, visible, ct, caller.UserId)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<MessageDto>>.Ok(dtos);
    }

    public async Task<Result<MessageDto>> EditMessageAsync(
        EditMessageCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new EditMessageValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var message = await messages.GetByIdAsync(command.MessageId, ct).ConfigureAwait(false);
        if (message is null || message.IsDeleted)
        {
            return Result<MessageDto>.Fail(MessageErrors.NotFound);
        }

        if (message.SenderId != caller.UserId)
        {
            return Result<MessageDto>.Fail(MessageErrors.NotSender);
        }

        var chat = await chats.GetByIdAsync(message.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<MessageDto>.Fail(member.Error);
        }

        var edited = message.Edit(command.Body, clock.UtcNow);
        if (edited.IsFailure)
        {
            return Result<MessageDto>.Fail(edited.Error);
        }

        await messages.UpdateAsync(message, ct).ConfigureAwait(false);
        return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, message, ct, caller.UserId).ConfigureAwait(false));
    }

    public async Task<Result<MessageDto>> DeleteMessageAsync(
        DeleteMessageCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new DeleteMessageValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var message = await messages.GetByIdAsync(command.MessageId, ct).ConfigureAwait(false);
        if (message is null || message.IsDeleted)
        {
            return Result<MessageDto>.Fail(MessageErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(message.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<MessageDto>.Fail(member.Error);
        }

        // Own messages: always. Others' messages: owners/admins (moderation).
        var isOwn = message.SenderId == caller.UserId;
        if (!isOwn && !member.Value!.IsAdmin(caller.UserId))
        {
            return Result<MessageDto>.Fail(MessageErrors.CannotDelete);
        }

        var deleted = message.Delete(caller.UserId, clock.UtcNow);
        if (deleted.IsFailure)
        {
            return Result<MessageDto>.Fail(deleted.Error);
        }

        await messages.UpdateAsync(message, ct).ConfigureAwait(false);

        // A pinned message that is deleted stays pinned until unpinned (Telegram-like).
        return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, message, ct, caller.UserId).ConfigureAwait(false));
    }

    public async Task<Result<MessageDto>> ForwardMessageAsync(
        ForwardMessageCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new ForwardMessageValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var senderId = caller.UserId;
        var existing = await messages.GetByClientKeyAsync(senderId, command.ClientMessageId.Trim(), ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ChatId != command.TargetChatId)
            {
                return Result<MessageDto>.Fail(MessageErrors.ClientIdConflict);
            }

            return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, existing, ct, senderId).ConfigureAwait(false));
        }

        var sourceChat = await chats.GetByIdAsync(command.SourceChatId, ct).ConfigureAwait(false);
        var sourceMember = ChatAccess.RequireMember(sourceChat, senderId);
        if (sourceMember.IsFailure)
        {
            return Result<MessageDto>.Fail(sourceMember.Error);
        }

        var source = await messages.GetByIdAsync(command.SourceMessageId, ct).ConfigureAwait(false);
        if (source is null || source.ChatId != command.SourceChatId || source.IsDeleted)
        {
            return Result<MessageDto>.Fail(MessageErrors.ForwardSourceNotFound);
        }

        var targetChat = await chats.GetByIdAsync(command.TargetChatId, ct).ConfigureAwait(false);
        var targetMember = ChatAccess.RequireCanPost(targetChat, senderId);
        if (targetMember.IsFailure)
        {
            return Result<MessageDto>.Fail(targetMember.Error);
        }

        var now = clock.UtcNow;
        var seq = await chats.AllocateSeqAsync(targetChat!.Id, now, ct).ConfigureAwait(false);
        // Forwards share the source bytes (no re-upload); the file stays linked
        // to its original message and is only enriched, never re-attached.
        var created = Message.Create(
            ids.NewId(), targetChat.Id, seq, command.ClientMessageId.Trim(), senderId,
            MessageKind.Forward, source.Body, null,
            new ForwardInfo(source.ChatId, source.Id, source.SenderId ?? "system", source.CreatedAt),
            now, source.FileId);
        if (created.IsFailure)
        {
            return Result<MessageDto>.Fail(created.Error);
        }

        try
        {
            await messages.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateMessageException)
        {
            var winner = await messages.GetByClientKeyAsync(senderId, command.ClientMessageId.Trim(), ct)
                .ConfigureAwait(false);
            if (winner is null)
            {
                throw;
            }

            return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, winner, ct, senderId).ConfigureAwait(false));
        }

        var preview = await ForwardPreviewAsync(files, source, ct).ConfigureAwait(false);
        await chats.ApplyNewMessageAsync(
            targetChat.Id, seq, created.Value!.Id, preview, senderId, now, ct).ConfigureAwait(false);

        return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, created.Value, ct, senderId).ConfigureAwait(false));
    }

    public async Task<Result<MessageDto>> ToggleReactionAsync(
        ToggleReactionCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new ToggleReactionValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var message = await messages.GetByIdAsync(command.MessageId, ct).ConfigureAwait(false);
        if (message is null || message.IsDeleted)
        {
            return Result<MessageDto>.Fail(MessageErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(message.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<MessageDto>.Fail(member.Error);
        }

        if (!member.Value!.CanReact(caller.UserId))
        {
            return Result<MessageDto>.Fail(MessageErrors.CannotReact);
        }

        var toggled = message.ToggleReaction(caller.UserId, command.Emoji, clock.UtcNow);
        if (toggled.IsFailure)
        {
            return Result<MessageDto>.Fail(toggled.Error);
        }

        await messages.UpdateAsync(message, ct).ConfigureAwait(false);
        return Result<MessageDto>.Ok(
            await MessageQueries.EnrichAsync(messages, files, users, message, ct, caller.UserId).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<MessageDto>>> GetRepliesAsync(
        GetRepliesQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetRepliesValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var parent = await messages.GetByIdAsync(query.MessageId, ct).ConfigureAwait(false);
        if (parent is null || parent.IsDeleted)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(MessageErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(parent.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(member.Error);
        }

        var rows = await messages.ListRepliesAsync(parent.ChatId, parent.Id, query.Limit, ct).ConfigureAwait(false);
        return Result<IReadOnlyList<MessageDto>>.Ok(
            await MessageQueries.EnrichManyAsync(messages, files, users, rows, ct, caller.UserId).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<MessageDto>>> BulkDeleteMessagesAsync(
        BulkDeleteMessagesCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new BulkDeleteMessagesValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = caller.UserId;
        var now = clock.UtcNow;
        var deleted = new List<MessageDto>();
        var deletedMessages = new List<Message>();

        // Group by chat so each conversation authorizes once. Targets arrive in
        // one round-trip (was: sequential GetById per selected id).
        var targets = (await messages.ListByIdsAsync(
                command.MessageIds.Distinct().Take(50).ToList(), ct).ConfigureAwait(false))
            .Where(m => !m.IsDeleted)
            .ToList();

        foreach (var group in targets.GroupBy(m => m.ChatId))
        {
            var chat = await chats.GetByIdAsync(group.Key, ct).ConfigureAwait(false);
            var member = ChatAccess.RequireMember(chat, callerId);
            if (member.IsFailure)
            {
                return Result<IReadOnlyList<MessageDto>>.Fail(member.Error);
            }

            // Authorize everything before mutating anything: a stale selection
            // must never leave a partial delete behind.
            var isAdmin = member.Value!.IsAdmin(callerId);
            foreach (var message in group)
            {
                if (message.SenderId != callerId && !isAdmin)
                {
                    return Result<IReadOnlyList<MessageDto>>.Fail(MessageErrors.CannotDelete);
                }
            }

            foreach (var message in group)
            {
                var removed = message.Delete(callerId, now);
                if (removed.IsFailure)
                {
                    continue;
                }

                await messages.UpdateAsync(message, ct).ConfigureAwait(false);
                deletedMessages.Add(message);
            }
        }

        // Enrich the deleted set in one batched pass (was: per-message enrich
        // with repeated sender lookups).
        var enriched = await MessageQueries.EnrichManyAsync(messages, files, users, deletedMessages, ct, callerId)
            .ConfigureAwait(false);
        deleted.AddRange(enriched);

        return Result<IReadOnlyList<MessageDto>>.Ok(deleted);
    }

    private static async Task<string> ForwardPreviewAsync(
        IFileStore fileStore, Message source, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(source.Body))
        {
            return Message.PreviewOf(source.Body);
        }

        if (source.FileId is not null)
        {
            var file = await fileStore.GetByIdAsync(source.FileId, ct).ConfigureAwait(false);
            if (file is not null)
            {
                return Domain.Files.FileValidation.PreviewLabel(file.Kind, file.FileName);
            }
        }

        return "[Attachment]";
    }
}
