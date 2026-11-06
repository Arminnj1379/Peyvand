using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Public channel subscriptions. Anyone may subscribe to a public channel;
/// private channels require an invite link (<see cref="JoinViaInviteCommand"/>)
/// or an admin add. Unsubscribing follows the same sole-owner guard as leaving.
/// </summary>
public sealed record SubscribeChannelCommand(string ChatId) : IRequest<Result<ChatDto>>;

public sealed record UnsubscribeChannelCommand(string ChatId) : IRequest<Result>;

public sealed record GetPublicChannelQuery(string Username) : IRequest<Result<PublicChannelDto>>;

public sealed class SubscribeChannelValidator : AbstractValidator<SubscribeChannelCommand>
{
    public SubscribeChannelValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class UnsubscribeChannelValidator : AbstractValidator<UnsubscribeChannelCommand>
{
    public UnsubscribeChannelValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetPublicChannelValidator : AbstractValidator<GetPublicChannelQuery>
{
    public GetPublicChannelValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MaximumLength(32);
    }
}

public sealed class SubscribeChannelHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SubscribeChannelCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(SubscribeChannelCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = currentUser.UserId;
        var caller = await users.GetByIdAsync(callerId, ct).ConfigureAwait(false);
        if (caller is null || !caller.IsActive)
        {
            return Result<ChatDto>.Fail(UserErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type != ChatType.Channel)
        {
            return Result<ChatDto>.Fail(ChatErrors.NotFound);
        }

        if (chat.IsMember(callerId))
        {
            return Result<ChatDto>.Fail(ChatErrors.AlreadySubscribed);
        }

        if (!chat.IsPublic)
        {
            return Result<ChatDto>.Fail(ChatErrors.InviteRequired);
        }

        var added = chat.AddMember(callerId, ChatRole.Subscriber, clock.UtcNow);
        if (added.IsFailure)
        {
            return Result<ChatDto>.Fail(added.Error);
        }

        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, chat, callerId, ct).ConfigureAwait(false));
    }
}

public sealed class UnsubscribeChannelHandler(
    IChatStore chats,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<UnsubscribeChannelCommand, Result>
{
    public async Task<Result> Handle(UnsubscribeChannelCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type != ChatType.Channel)
        {
            return Result.Fail(ChatErrors.NotFound);
        }

        if (!chat.IsMember(currentUser.UserId))
        {
            return Result.Fail(ChatErrors.NotSubscribed);
        }

        var left = chat.RemoveMember(currentUser.UserId, clock.UtcNow);
        if (left.IsFailure)
        {
            return left;
        }

        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}

public sealed class GetPublicChannelHandler(
    IChatStore chats) : IRequestHandler<GetPublicChannelQuery, Result<PublicChannelDto>>
{
    public async Task<Result<PublicChannelDto>> Handle(GetPublicChannelQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var chat = await chats.GetByUsernameAsync(request.Username.Trim(), ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type != ChatType.Channel || !chat.IsPublic)
        {
            return Result<PublicChannelDto>.Fail(ChatErrors.NotFound);
        }

        return Result<PublicChannelDto>.Ok(MessagingMapping.ToPublicDto(chat));
    }
}
