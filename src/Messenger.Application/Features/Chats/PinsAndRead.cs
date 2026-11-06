using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Chats;

public sealed record PinMessageCommand(string ChatId, string MessageId) : IRequest<Result<ChatDto>>;

public sealed record UnpinMessageCommand(string ChatId, string MessageId) : IRequest<Result<ChatDto>>;

public sealed record MarkReadCommand(string ChatId, long Seq) : IRequest<Result<ChatDto>>;

public sealed class PinMessageValidator : AbstractValidator<PinMessageCommand>
{
    public PinMessageValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
    }
}

public sealed class UnpinMessageValidator : AbstractValidator<UnpinMessageCommand>
{
    public UnpinMessageValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
    }
}

public sealed class MarkReadValidator : AbstractValidator<MarkReadCommand>
{
    public MarkReadValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Seq).GreaterThanOrEqualTo(0);
    }
}

public sealed class PinMessageHandler(
    IChatStore chats,
    IMessageStore messages,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<PinMessageCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(PinMessageCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        // Pinning is a moderation action: members can pin in DMs, admins in groups/channels.
        var authorized = chat?.Type == Domain.Chats.ChatType.Dm
            ? ChatAccess.RequireMember(chat, currentUser.UserId)
            : ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (authorized.IsFailure)
        {
            return Result<ChatDto>.Fail(authorized.Error);
        }

        var message = await messages.GetByIdAsync(request.MessageId, ct).ConfigureAwait(false);
        if (message is null || message.ChatId != request.ChatId || message.IsDeleted)
        {
            return Result<ChatDto>.Fail(MessageErrors.NotFound);
        }

        var pinned = authorized.Value!.Pin(message.Id, clock.UtcNow);
        if (pinned.IsFailure)
        {
            return Result<ChatDto>.Fail(pinned.Error);
        }

        await chats.UpdateAsync(authorized.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, authorized.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class UnpinMessageHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<UnpinMessageCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(UnpinMessageCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var authorized = chat?.Type == Domain.Chats.ChatType.Dm
            ? ChatAccess.RequireMember(chat, currentUser.UserId)
            : ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (authorized.IsFailure)
        {
            return Result<ChatDto>.Fail(authorized.Error);
        }

        var unpinned = authorized.Value!.Unpin(request.MessageId, clock.UtcNow);
        if (unpinned.IsFailure)
        {
            return Result<ChatDto>.Fail(unpinned.Error);
        }

        await chats.UpdateAsync(authorized.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, authorized.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class MarkReadHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<MarkReadCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(MarkReadCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<ChatDto>.Fail(member.Error);
        }

        var now = clock.UtcNow;
        // Atomic compare-and-set in the store (safe under concurrent readers);
        // the in-memory copy feeds the response projection.
        await chats.MarkReadAsync(chat!.Id, currentUser.UserId, request.Seq, now, ct).ConfigureAwait(false);
        member.Value!.MarkRead(currentUser.UserId, request.Seq, now);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}
