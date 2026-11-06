using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Per-member conversation preferences: mute, archive, and mark-unread.
/// All three are member-only and caller-relative — they never affect other
/// participants and need no admin rights.
/// </summary>
public sealed record SetMuteCommand(string ChatId, bool Muted) : IRequest<Result<ChatDto>>;

public sealed record SetArchiveCommand(string ChatId, bool Archived) : IRequest<Result<ChatDto>>;

public sealed record MarkUnreadCommand(string ChatId, long Seq) : IRequest<Result<ChatDto>>;

public sealed class SetMuteValidator : AbstractValidator<SetMuteCommand>
{
    public SetMuteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class SetArchiveValidator : AbstractValidator<SetArchiveCommand>
{
    public SetArchiveValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class MarkUnreadValidator : AbstractValidator<MarkUnreadCommand>
{
    public MarkUnreadValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Seq).GreaterThanOrEqualTo(1);
    }
}

public sealed class SetMuteHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SetMuteCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(SetMuteCommand request, CancellationToken ct)
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

        var updated = member.Value!.SetMemberMute(currentUser.UserId, request.Muted, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class SetArchiveHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SetArchiveCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(SetArchiveCommand request, CancellationToken ct)
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

        var updated = member.Value!.SetMemberArchived(currentUser.UserId, request.Archived, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class MarkUnreadHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<MarkUnreadCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(MarkUnreadCommand request, CancellationToken ct)
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

        var updated = member.Value!.MarkUnread(currentUser.UserId, request.Seq, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}
