using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

public sealed record EditMessageCommand(string MessageId, string Body) : IRequest<Result<MessageDto>>;

public sealed record DeleteMessageCommand(string MessageId) : IRequest<Result<MessageDto>>;

public sealed class EditMessageValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
        // Empty captions are legal on attachment messages; the domain rejects
        // empty text-only messages (MessageErrors.InvalidBody).
        RuleFor(x => x.Body).NotNull().MaximumLength(Message.MaxBodyLength);
    }
}

public sealed class DeleteMessageValidator : AbstractValidator<DeleteMessageCommand>
{
    public DeleteMessageValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
    }
}

public sealed class EditMessageHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<EditMessageCommand, Result<MessageDto>>
{
    public async Task<Result<MessageDto>> Handle(EditMessageCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var message = await messages.GetByIdAsync(request.MessageId, ct).ConfigureAwait(false);
        if (message is null || message.IsDeleted)
        {
            return Result<MessageDto>.Fail(MessageErrors.NotFound);
        }

        if (message.SenderId != currentUser.UserId)
        {
            return Result<MessageDto>.Fail(MessageErrors.NotSender);
        }

        var chat = await chats.GetByIdAsync(message.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<MessageDto>.Fail(member.Error);
        }

        var edited = message.Edit(request.Body, clock.UtcNow);
        if (edited.IsFailure)
        {
            return Result<MessageDto>.Fail(edited.Error);
        }

        await messages.UpdateAsync(message, ct).ConfigureAwait(false);
        return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, message, ct, currentUser.UserId).ConfigureAwait(false));
    }
}

public sealed class DeleteMessageHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<DeleteMessageCommand, Result<MessageDto>>
{
    public async Task<Result<MessageDto>> Handle(DeleteMessageCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<MessageDto>.Fail(ChatAccess.Unauthenticated());
        }

        var message = await messages.GetByIdAsync(request.MessageId, ct).ConfigureAwait(false);
        if (message is null || message.IsDeleted)
        {
            return Result<MessageDto>.Fail(MessageErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(message.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<MessageDto>.Fail(member.Error);
        }

        // Own messages: always. Others' messages: owners/admins (moderation).
        var isOwn = message.SenderId == currentUser.UserId;
        if (!isOwn && !member.Value!.IsAdmin(currentUser.UserId))
        {
            return Result<MessageDto>.Fail(MessageErrors.CannotDelete);
        }

        var deleted = message.Delete(currentUser.UserId, clock.UtcNow);
        if (deleted.IsFailure)
        {
            return Result<MessageDto>.Fail(deleted.Error);
        }

        await messages.UpdateAsync(message, ct).ConfigureAwait(false);

        // A pinned message that is deleted stays pinned until unpinned (Telegram-like).
        return Result<MessageDto>.Ok(await MessageQueries.EnrichAsync(messages, files, users, message, ct, currentUser.UserId).ConfigureAwait(false));
    }
}
