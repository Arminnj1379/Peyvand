using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Chats;

/// <summary>Direct conversation. Idempotent: same pair returns the existing DM.</summary>
public sealed record CreateDmCommand(string OtherUserId) : IRequest<Result<ChatDto>>;

public sealed class CreateDmValidator : AbstractValidator<CreateDmCommand>
{
    public CreateDmValidator()
    {
        RuleFor(x => x.OtherUserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class CreateDmHandler(
    IChatStore chats,
    IUserStore users,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<CreateDmCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(CreateDmCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = currentUser.UserId;
        if (request.OtherUserId == callerId)
        {
            return Result<ChatDto>.Fail(ChatErrors.CannotDmSelf);
        }

        var other = await users.GetByIdAsync(request.OtherUserId, ct).ConfigureAwait(false);
        if (other is null || !other.IsActive)
        {
            return Result<ChatDto>.Fail(UserErrors.NotFound);
        }

        // Blocks are enforced in both directions without revealing who blocked:
        // no new DM can be started across a block.
        var caller = await users.GetByIdAsync(callerId, ct).ConfigureAwait(false);
        if (caller is not null &&
            (caller.BlockedUserIds.Contains(other.Id) || other.BlockedUserIds.Contains(callerId)))
        {
            return Result<ChatDto>.Fail(ChatErrors.Blocked);
        }

        var existing = await chats.FindDmAsync(callerId, other.Id, ct).ConfigureAwait(false);
        if (existing is not null && !existing.IsDeleted)
        {
            return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, existing, callerId, ct).ConfigureAwait(false));
        }

        var now = clock.UtcNow;
        var created = Chat.CreateDm(ids.NewId(), callerId, other.Id, now);
        if (created.IsFailure)
        {
            return Result<ChatDto>.Fail(created.Error);
        }

        try
        {
            await chats.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateChatException)
        {
            // Lost the insert race on the deterministic pair key: the winner
            // holds the conversation — return it (idempotent create).
            var winner = await chats.FindDmAsync(callerId, other.Id, ct).ConfigureAwait(false);
            if (winner is null || winner.IsDeleted)
            {
                return Result<ChatDto>.Fail(ChatErrors.DmExists);
            }

            return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, winner, callerId, ct).ConfigureAwait(false));
        }

        return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, created.Value!, callerId, ct).ConfigureAwait(false));
    }
}
