using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Emoji reactions (toggle) and reply threads (direct children, oldest-first).
/// Authorization: every member — including channel subscribers, who cannot post
/// — may react and read threads. Reactions on deleted messages are rejected.
/// </summary>
public sealed record ToggleReactionCommand(string MessageId, string Emoji) : IRequest<Result<MessageDto>>;

public sealed record GetRepliesQuery(string MessageId, int Limit)
    : IRequest<Result<IReadOnlyList<MessageDto>>>;

public sealed class ToggleReactionValidator : AbstractValidator<ToggleReactionCommand>
{
    public ToggleReactionValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Emoji).NotEmpty().MinimumLength(1).MaximumLength(16);
    }
}

public sealed class GetRepliesValidator : AbstractValidator<GetRepliesQuery>
{
    public GetRepliesValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class ToggleReactionHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<ToggleReactionCommand, Result<MessageDto>>
{
    public async Task<Result<MessageDto>> Handle(ToggleReactionCommand request, CancellationToken ct)
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

        if (!member.Value!.CanReact(currentUser.UserId))
        {
            return Result<MessageDto>.Fail(MessageErrors.CannotReact);
        }

        var toggled = message.ToggleReaction(currentUser.UserId, request.Emoji, clock.UtcNow);
        if (toggled.IsFailure)
        {
            return Result<MessageDto>.Fail(toggled.Error);
        }

        await messages.UpdateAsync(message, ct).ConfigureAwait(false);
        return Result<MessageDto>.Ok(
            await MessageQueries.EnrichAsync(messages, files, users, message, ct, currentUser.UserId).ConfigureAwait(false));
    }
}

public sealed class GetRepliesHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<GetRepliesQuery, Result<IReadOnlyList<MessageDto>>>
{
    public async Task<Result<IReadOnlyList<MessageDto>>> Handle(GetRepliesQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var parent = await messages.GetByIdAsync(request.MessageId, ct).ConfigureAwait(false);
        if (parent is null || parent.IsDeleted)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(MessageErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(parent.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(member.Error);
        }

        var rows = await messages.ListRepliesAsync(parent.ChatId, parent.Id, request.Limit, ct).ConfigureAwait(false);
        return Result<IReadOnlyList<MessageDto>>.Ok(
            await MessageQueries.EnrichManyAsync(messages, files, users, rows, ct, currentUser.UserId).ConfigureAwait(false));
    }
}
