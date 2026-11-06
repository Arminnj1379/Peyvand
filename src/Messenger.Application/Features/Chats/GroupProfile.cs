using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Group and channel profile management. All mutations are admin-only;
/// the domain rejects cross-type misuse (e.g. group settings on a channel).
/// </summary>
public sealed record UpdateGroupCommand(
    string ChatId,
    string? Title,
    string? Description,
    bool? MembersCanInvite) : IRequest<Result<ChatDto>>;

public sealed record UpdateChannelCommand(
    string ChatId,
    string? Title,
    string? Description) : IRequest<Result<ChatDto>>;

public sealed record SetChannelVisibilityCommand(
    string ChatId,
    bool IsPublic,
    string? Username) : IRequest<Result<ChatDto>>;

public sealed class UpdateGroupValidator : AbstractValidator<UpdateGroupCommand>
{
    public UpdateGroupValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Title).MinimumLength(1).MaximumLength(64)
            .When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(512)
            .When(x => x.Description is not null);
    }
}

public sealed class UpdateChannelValidator : AbstractValidator<UpdateChannelCommand>
{
    public UpdateChannelValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Title).MinimumLength(1).MaximumLength(64)
            .When(x => x.Title is not null);
        RuleFor(x => x.Description).MaximumLength(512)
            .When(x => x.Description is not null);
    }
}

public sealed class SetChannelVisibilityValidator : AbstractValidator<SetChannelVisibilityCommand>
{
    public SetChannelVisibilityValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Username).MaximumLength(32)
            .When(x => !string.IsNullOrWhiteSpace(x.Username));
    }
}

public sealed class UpdateGroupHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<UpdateGroupCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(UpdateGroupCommand request, CancellationToken ct)
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

        var updated = admin.Value!.UpdateGroupProfile(
            request.Title, request.Description, request.MembersCanInvite, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class UpdateChannelHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<UpdateChannelCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(UpdateChannelCommand request, CancellationToken ct)
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

        var updated = admin.Value!.UpdateChannelProfile(request.Title, request.Description, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class SetChannelVisibilityHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SetChannelVisibilityCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(SetChannelVisibilityCommand request, CancellationToken ct)
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

        if (request.IsPublic)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || !Chat.IsValidUsername(request.Username))
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameInvalid);
            }

            // Self-claim is fine; anyone else holding it is a conflict.
            var holder = await chats.GetByUsernameAsync(request.Username, ct).ConfigureAwait(false);
            if (holder is not null && holder.Id != admin.Value!.Id)
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
            }
        }

        var updated = admin.Value!.SetVisibility(request.IsPublic, request.Username, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        try
        {
            await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        }
        catch (DuplicateChatException)
        {
            return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
        }

        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}
