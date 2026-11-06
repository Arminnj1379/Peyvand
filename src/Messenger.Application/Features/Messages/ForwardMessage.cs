using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

/// <summary>Copies a message into another conversation with provenance attached.</summary>
public sealed record ForwardMessageCommand(
    string SourceChatId,
    string SourceMessageId,
    string TargetChatId,
    string ClientMessageId) : IRequest<Result<MessageDto>>;

public sealed class ForwardMessageValidator : AbstractValidator<ForwardMessageCommand>
{
    public ForwardMessageValidator()
    {
        RuleFor(x => x.SourceChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.SourceMessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.TargetChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ClientMessageId).NotEmpty().MaximumLength(64);
    }
}

public sealed class ForwardMessageHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<ForwardMessageCommand, Result<MessageDto>>
{
    public async Task<Result<MessageDto>> Handle(ForwardMessageCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var senderId = currentUser.UserId;
        var existing = await messages.GetByClientKeyAsync(senderId, request.ClientMessageId.Trim(), ct)
            .ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.ChatId != request.TargetChatId)
            {
                return Result<MessageDto>.Fail(MessageErrors.ClientIdConflict);
            }

            return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, existing, ct, senderId).ConfigureAwait(false));
        }

        var sourceChat = await chats.GetByIdAsync(request.SourceChatId, ct).ConfigureAwait(false);
        var sourceMember = ChatAccess.RequireMember(sourceChat, senderId);
        if (sourceMember.IsFailure)
        {
            return Result<MessageDto>.Fail(sourceMember.Error);
        }

        var source = await messages.GetByIdAsync(request.SourceMessageId, ct).ConfigureAwait(false);
        if (source is null || source.ChatId != request.SourceChatId || source.IsDeleted)
        {
            return Result<MessageDto>.Fail(MessageErrors.ForwardSourceNotFound);
        }

        var targetChat = await chats.GetByIdAsync(request.TargetChatId, ct).ConfigureAwait(false);
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
            ids.NewId(), targetChat.Id, seq, request.ClientMessageId.Trim(), senderId,
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
            var winner = await messages.GetByClientKeyAsync(senderId, request.ClientMessageId.Trim(), ct)
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

    private static async Task<string> ForwardPreviewAsync(
        IFileStore files, Message source, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(source.Body))
        {
            return Message.PreviewOf(source.Body);
        }

        if (source.FileId is not null)
        {
            var file = await files.GetByIdAsync(source.FileId, ct).ConfigureAwait(false);
            if (file is not null)
            {
                return Domain.Files.FileValidation.PreviewLabel(file.Kind, file.FileName);
            }
        }

        return "[Attachment]";
    }
}
