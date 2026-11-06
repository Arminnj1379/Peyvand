using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Chats;

/// <summary>Participant management. Groups/channels only; DMs are fixed pairs.</summary>
public sealed record AddMemberCommand(string ChatId, string UserId) : IRequest<Result<ChatDto>>;

public sealed record RemoveMemberCommand(string ChatId, string UserId) : IRequest<Result<ChatDto>>;

public sealed record LeaveChatCommand(string ChatId) : IRequest<Result>;

public sealed record SetMemberRoleCommand(string ChatId, string UserId, string Role) : IRequest<Result<ChatDto>>;

public sealed class AddMemberValidator : AbstractValidator<AddMemberCommand>
{
    public AddMemberValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RemoveMemberValidator : AbstractValidator<RemoveMemberCommand>
{
    public RemoveMemberValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class LeaveChatValidator : AbstractValidator<LeaveChatCommand>
{
    public LeaveChatValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class SetMemberRoleValidator : AbstractValidator<SetMemberRoleCommand>
{
    public SetMemberRoleValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Role).Must(r => Enum.TryParse<ChatRole>(r, ignoreCase: true, out _))
            .WithMessage("Unknown role: '{PropertyValue}'.");
    }
}

public sealed class AddMemberHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<AddMemberCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(AddMemberCommand request, CancellationToken ct)
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

        // Admins always; plain members only when the group allows member invites.
        // (Channels never allow subscriber invites — admins only.)
        if (!member.Value!.CanInvite(currentUser.UserId))
        {
            return Result<ChatDto>.Fail(ChatErrors.NotAdmin);
        }

        var admin = member;

        var target = await users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive)
        {
            return Result<ChatDto>.Fail(UserErrors.NotFound);
        }

        var role = chat!.Type == ChatType.Channel ? ChatRole.Subscriber : ChatRole.Member;
        var added = admin.Value!.AddMember(target.Id, role, clock.UtcNow);
        if (added.IsFailure)
        {
            return Result<ChatDto>.Fail(added.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class RemoveMemberHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<RemoveMemberCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(RemoveMemberCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        var removed = admin.Value!.RemoveMember(request.UserId, clock.UtcNow);
        if (removed.IsFailure)
        {
            return Result<ChatDto>.Fail(removed.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class LeaveChatHandler(
    IChatStore chats,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<LeaveChatCommand, Result>
{
    public async Task<Result> Handle(LeaveChatCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result.Fail(member.Error);
        }

        var left = member.Value!.RemoveMember(currentUser.UserId, clock.UtcNow);
        if (left.IsFailure)
        {
            return left;
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}

public sealed class SetMemberRoleHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SetMemberRoleCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(SetMemberRoleCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        if (!Enum.TryParse<ChatRole>(request.Role, ignoreCase: true, out var role))
        {
            return Result<ChatDto>.Fail(ChatErrors.InvalidRole);
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        var updated = admin.Value!.SetMemberRole(request.UserId, role, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}
