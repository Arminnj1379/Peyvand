using System.Security.Cryptography;
using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Revocable invite links for groups and channels (DMs have none).
/// Access control: any member may view the active link so it can be shared;
/// only admins may rotate or revoke it; anyone holding a valid link may join
/// (groups as Member, channels as Subscriber).
/// </summary>
public sealed record GetInviteQuery(string ChatId) : IRequest<Result<string?>>;

public sealed record RotateInviteCommand(string ChatId) : IRequest<Result<string>>;

public sealed record RevokeInviteCommand(string ChatId) : IRequest<Result>;

public sealed record JoinViaInviteCommand(string Code) : IRequest<Result<ChatDto>>;

public sealed class GetInviteValidator : AbstractValidator<GetInviteQuery>
{
    public GetInviteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RotateInviteValidator : AbstractValidator<RotateInviteCommand>
{
    public RotateInviteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RevokeInviteValidator : AbstractValidator<RevokeInviteCommand>
{
    public RevokeInviteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class JoinViaInviteValidator : AbstractValidator<JoinViaInviteCommand>
{
    public JoinViaInviteValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MinimumLength(8).MaximumLength(128);
    }
}

public sealed class GetInviteHandler(
    IChatStore chats,
    ICurrentUser currentUser) : IRequestHandler<GetInviteQuery, Result<string?>>
{
    public async Task<Result<string?>> Handle(GetInviteQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<string?>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<string?>.Fail(member.Error);
        }

        return Result<string?>.Ok(member.Value!.InviteCode);
    }
}

public sealed class RotateInviteHandler(
    IChatStore chats,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<RotateInviteCommand, Result<string>>
{
    public async Task<Result<string>> Handle(RotateInviteCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<string>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (admin.IsFailure)
        {
            return Result<string>.Fail(admin.Error);
        }

        if (admin.Value!.Type == ChatType.Dm)
        {
            return Result<string>.Fail(ChatErrors.DmMembershipFixed);
        }

        // Rotation loop is bounded: codes are 128-bit, collisions are theoretical.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var clash = await chats.GetByInviteCodeAsync(code, ct).ConfigureAwait(false);
            if (clash is not null && clash.Id != admin.Value.Id)
            {
                continue;
            }

            var rotated = admin.Value.RotateInviteCode(code, clock.UtcNow);
            if (rotated.IsFailure)
            {
                return Result<string>.Fail(rotated.Error);
            }

            try
            {
                await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
            }
            catch (DuplicateChatException)
            {
                continue;
            }

            return Result<string>.Ok(code);
        }

        return Result<string>.Fail(ChatErrors.MemberLimit);
    }
}

public sealed class RevokeInviteHandler(
    IChatStore chats,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<RevokeInviteCommand, Result>
{
    public async Task<Result> Handle(RevokeInviteCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (admin.IsFailure)
        {
            return Result.Fail(admin.Error);
        }

        admin.Value!.RevokeInviteCode(clock.UtcNow);
        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}

public sealed class JoinViaInviteHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<JoinViaInviteCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(JoinViaInviteCommand request, CancellationToken ct)
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

        var chat = await chats.GetByInviteCodeAsync(request.Code.Trim(), ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type == ChatType.Dm)
        {
            // Neutral: revoked and never-existed codes look identical.
            return Result<ChatDto>.Fail(ChatErrors.InviteInvalid);
        }

        if (chat.IsMember(callerId))
        {
            return Result<ChatDto>.Ok(
                await ChatQueries.EnrichAsync(chats, users, chat, callerId, ct).ConfigureAwait(false));
        }

        var role = chat.Type == ChatType.Channel ? ChatRole.Subscriber : ChatRole.Member;
        var added = chat.AddMember(callerId, role, clock.UtcNow);
        if (added.IsFailure)
        {
            return Result<ChatDto>.Fail(added.Error);
        }

        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, chat, callerId, ct).ConfigureAwait(false));
    }
}
