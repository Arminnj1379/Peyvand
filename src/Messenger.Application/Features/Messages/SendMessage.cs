using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Sends a text or attachment message. Idempotent on <c>(sender, clientMessageId)</c>:
/// retries return the original message, never a duplicate.
/// Captions are optional when a file is attached; text messages still need a body.
/// Persist-then-broadcast: the endpoint fans out only after this succeeds.
/// </summary>
public sealed record SendMessageCommand(
    string ChatId,
    string Body,
    string ClientMessageId,
    string? ReplyToMessageId,
    string? FileId = null) : IRequest<Result<MessageDto>>;

public sealed class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        // Empty bodies are legal captions on attachment messages; the domain
        // rejects empty text-only messages (MessageErrors.InvalidBody).
        RuleFor(x => x.Body).NotNull().MaximumLength(Message.MaxBodyLength);
        RuleFor(x => x.ClientMessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ReplyToMessageId).MaximumLength(64);
        RuleFor(x => x.FileId).MaximumLength(64);
    }
}

public sealed class SendMessageHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SendMessageCommand, Result<MessageDto>>
{
    public async Task<Result<MessageDto>> Handle(SendMessageCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var senderId = currentUser.UserId;

        // Idempotency fast path: the client retried (same sender + key).
        var existing = await messages.GetByClientKeyAsync(senderId, request.ClientMessageId.Trim(), ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ChatId != request.ChatId)
            {
                return Result<MessageDto>.Fail(MessageErrors.ClientIdConflict);
            }

            return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, existing, ct, senderId).ConfigureAwait(false));
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
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
        if (!string.IsNullOrWhiteSpace(request.ReplyToMessageId))
        {
            var target = await messages.GetByIdAsync(request.ReplyToMessageId, ct).ConfigureAwait(false);
            if (target is null || target.ChatId != request.ChatId || target.IsDeleted)
            {
                return Result<MessageDto>.Fail(MessageErrors.ReplyTargetNotFound);
            }

            replyTo = target.Id;
            kind = MessageKind.Reply;
        }

        // Attachments must be the sender's own ready, unattached uploads.
        Domain.Files.ChatFile? upload = null;
        if (!string.IsNullOrWhiteSpace(request.FileId))
        {
            upload = await files.GetByIdAsync(request.FileId, ct).ConfigureAwait(false);
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
            ids.NewId(), chat.Id, seq, request.ClientMessageId.Trim(),
            senderId, kind, request.Body, replyTo, null, now, upload?.Id);
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
            var winner = await messages.GetByClientKeyAsync(senderId, request.ClientMessageId.Trim(), ct)
                .ConfigureAwait(false);
            if (winner is null)
            {
                throw;
            }

            if (winner.ChatId != request.ChatId)
            {
                return Result<MessageDto>.Fail(MessageErrors.ClientIdConflict);
            }

            return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, winner, ct, senderId).ConfigureAwait(false));
        }

        var preview = string.IsNullOrWhiteSpace(request.Body) && upload is not null
            ? Domain.Files.FileValidation.PreviewLabel(upload.Kind, upload.FileName)
            : Message.PreviewOf(request.Body);
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
}
