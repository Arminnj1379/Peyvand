using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Users;
using Messenger.Application.Services;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Services.Implementations;

/// <summary>
/// Profile, avatar, settings, directory, and block use cases. Logic moved
/// verbatim from the former MediatR handlers.
/// </summary>
public sealed class UserService(
    IUserStore users,
    IAvatarStorage avatars,
    IChatStore chats,
    IClock clock) : IUserService
{
    public async Task<Result<UserDto>> GetMyProfileAsync(CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }

    public async Task<Result<UserDto>> UpdateMyProfileAsync(
        UpdateMyProfileCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UpdateMyProfileValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        var updated = user.UpdateProfile(command.DisplayName, command.Bio, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<UserDto>.Fail(updated.Error);
        }

        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }

    public async Task<Result<UserDto>> UploadAvatarAsync(
        UploadAvatarCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UploadAvatarValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        if (!AvatarValidation.HasValidMagicNumber(command.Bytes, command.ContentType))
        {
            return Result<UserDto>.Fail(UserErrors.AvatarUnsupportedType);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        var reference = await avatars.SaveAsync(user.Id, command.ContentType, command.Bytes, ct)
            .ConfigureAwait(false);

        if (user.AvatarObjectKey is not null && user.AvatarObjectKey != reference.ObjectKey)
        {
            await avatars.DeleteAsync(user.AvatarObjectKey, ct).ConfigureAwait(false);
        }

        var url = $"/api/v1/users/{user.Id}/avatar?v={Uri.EscapeDataString(reference.ObjectKey)}";
        user.SetAvatar(reference.ObjectKey, url, clock.UtcNow);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }

    public async Task<Result<UserDto>> RemoveAvatarAsync(CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (user.AvatarObjectKey is not null)
        {
            await avatars.DeleteAsync(user.AvatarObjectKey, ct).ConfigureAwait(false);
        }

        user.RemoveAvatar(clock.UtcNow);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }

    public async Task<Result<SettingsDto>> GetMySettingsAsync(CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.UserId is null)
        {
            return Result<SettingsDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<SettingsDto>.Fail(UserErrors.NotFound);
        }

        return Result<SettingsDto>.Ok(new SettingsDto(user.Theme, user.NotificationsEnabled));
    }

    public async Task<Result<SettingsDto>> UpdateMySettingsAsync(
        UpdateMySettingsCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UpdateMySettingsValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<SettingsDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<SettingsDto>.Fail(UserErrors.NotFound);
        }

        var updated = user.UpdateSettings(command.Theme, command.NotificationsEnabled, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<SettingsDto>.Fail(updated.Error);
        }

        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<SettingsDto>.Ok(new SettingsDto(user.Theme, user.NotificationsEnabled));
    }

    public async Task<Result<IReadOnlyList<UserDto>>> SearchUsersAsync(
        SearchUsersQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new SearchUsersValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<UserDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var callerIsEmployee = ProfileVisibility.IsEmployee(caller);

        var found = await users.SearchAsync(query.Query.Trim(), callerIsEmployee, query.Limit, ct)
            .ConfigureAwait(false);
        var dtos = found
            .Where(u => u.IsActive)
            .Select(u => UserMapping.ToDto(u, includePhone: u.Id == caller.UserId))
            .ToList();
        return Result<IReadOnlyList<UserDto>>.Ok(dtos);
    }

    public async Task<Result<UserDto>> GetUserByIdAsync(
        GetUserByIdQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetUserByIdValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var target = await users.GetByIdAsync(query.UserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (!await ProfileVisibility.CanViewAsync(caller, target, chats, ct).ConfigureAwait(false))
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        var isSelf = target.Id == caller.UserId;
        return Result<UserDto>.Ok(UserMapping.ToDto(target, includePhone: isSelf));
    }

    public async Task<Result<AvatarFileDto>> GetAvatarAsync(
        GetAvatarQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetAvatarValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<AvatarFileDto>.Fail(AuthErrors.Unauthenticated);
        }

        var target = await users.GetByIdAsync(query.UserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive || target.AvatarObjectKey is null)
        {
            return Result<AvatarFileDto>.Fail(UserErrors.NotFound);
        }

        if (!await ProfileVisibility.CanViewAsync(caller, target, chats, ct).ConfigureAwait(false))
        {
            return Result<AvatarFileDto>.Fail(UserErrors.NotFound);
        }

        var file = await avatars.LoadAsync(target.AvatarObjectKey, ct).ConfigureAwait(false);
        if (file is null)
        {
            return Result<AvatarFileDto>.Fail(UserErrors.NotFound);
        }

        return Result<AvatarFileDto>.Ok(new AvatarFileDto(file.Value.Bytes, file.Value.ContentType, target.AvatarObjectKey));
    }

    public async Task<Result> BlockUserAsync(
        BlockUserCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new BlockUserValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Fail(UserErrors.NotFound);
        }

        var target = await users.GetByIdAsync(command.TargetUserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive)
        {
            return Result.Fail(UserErrors.NotFound);
        }

        var blocked = user.Block(target.Id, clock.UtcNow);
        if (blocked.IsFailure)
        {
            return blocked;
        }

        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result> UnblockUserAsync(
        UnblockUserCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UnblockUserValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Fail(UserErrors.NotFound);
        }

        user.Unblock(command.TargetUserId, clock.UtcNow);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
