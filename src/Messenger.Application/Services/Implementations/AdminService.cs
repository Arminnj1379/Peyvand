using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Admin;
using Messenger.Application.Features.Auth;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Services.Implementations;

/// <summary>Internal employee management. Logic moved verbatim from the former MediatR handlers.</summary>
public sealed class AdminService(
    IUserStore users,
    IDeviceSessionStore sessions,
    IPasswordHasher passwords,
    IIdGenerator ids,
    IClock clock) : IAdminService
{
    private static readonly Role[] AdminOnlyRoles = [Role.SystemAdministrator, Role.OrganizationAdministrator];

    public async Task<Result<UserDto>> CreateEmployeeAsync(
        CreateEmployeeCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new CreateEmployeeValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        var callerIsSysAdmin = caller.HasRole(nameof(Role.SystemAdministrator));
        var callerIsOrgAdmin = caller.HasRole(nameof(Role.OrganizationAdministrator));
        if (!callerIsSysAdmin && !callerIsOrgAdmin)
        {
            return Result<UserDto>.Fail(AuthErrors.Forbidden);
        }

        var roles = new List<Role>();
        foreach (var name in command.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<Role>(name, ignoreCase: true, out var role))
            {
                return Result<UserDto>.Fail(new Error("user.unknown_role", $"Unknown role: '{name}'."));
            }

            if (AdminOnlyRoles.Contains(role) && !callerIsSysAdmin)
            {
                return Result<UserDto>.Fail(AuthErrors.CannotGrantRole);
            }

            if (role != Role.Employee && role != Role.Customer)
            {
                roles.Add(role);
            }
        }

        var now = clock.UtcNow;
        var username = User.NormalizeUsername(command.Username);
        if (await users.ExistsByUsernameAsync(username, ct).ConfigureAwait(false))
        {
            return Result<UserDto>.Fail(UserErrors.UsernameTaken);
        }

        var phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim();
        if (phone is not null && await users.ExistsByPhoneAsync(phone, ct).ConfigureAwait(false))
        {
            return Result<UserDto>.Fail(UserErrors.PhoneTaken);
        }

        var registered = User.Register(
            ids.NewId(), username, command.DisplayName, passwords.Hash(command.Password),
            UserType.Employee, roles, phone, now);
        if (registered.IsFailure)
        {
            return Result<UserDto>.Fail(registered.Error);
        }

        var user = registered.Value!;
        try
        {
            await users.AddAsync(user, ct).ConfigureAwait(false);
        }
        catch (DuplicateUserException ex)
        {
            // Same race contract as customer registration: conflicting claim
            // surfaces as a 409 result, not an infrastructure exception.
            return Result<UserDto>.Fail(ex.Error);
        }

        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }

    public async Task<Result<IReadOnlyList<UserDto>>> ListUsersAsync(
        ListUsersQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new ListUsersValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        var gate = AdminGate.RequireAdmin(caller);
        if (gate.IsFailure)
        {
            return Result<IReadOnlyList<UserDto>>.Fail(gate.Error);
        }

        var found = await users.ListAsync(query.UserType, query.Role, query.IsActive, query.Cursor, query.Limit, ct)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<UserDto>>.Ok(
            found.Select(u => UserMapping.ToDto(u, includePhone: true)).ToList());
    }

    public async Task<Result<UserDto>> SetUserActiveAsync(
        SetUserActiveCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SetUserActiveValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        var gate = AdminGate.RequireAdmin(caller);
        if (gate.IsFailure)
        {
            return Result<UserDto>.Fail(gate.Error);
        }

        var target = await users.GetByIdAsync(command.UserId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (target.Id == caller.UserId)
        {
            return Result<UserDto>.Fail(new Error("user.cannot_change_self", "You cannot change your own active state."));
        }

        if (AdminGate.IsProtectedAdmin(target) &&
            !caller.HasRole(nameof(Role.SystemAdministrator)))
        {
            return Result<UserDto>.Fail(AuthErrors.CannotGrantRole);
        }

        var now = clock.UtcNow;
        if (command.IsActive)
        {
            target.Activate(now);
        }
        else
        {
            target.Deactivate(now);
            await sessions.RevokeAllForUserAsync(target.Id, now, ct).ConfigureAwait(false);
        }

        await users.UpdateAsync(target, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(target, includePhone: true));
    }

    public async Task<Result<UserDto>> SetUserRolesAsync(
        SetUserRolesCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SetUserRolesValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        var gate = AdminGate.RequireAdmin(caller);
        if (gate.IsFailure)
        {
            return Result<UserDto>.Fail(gate.Error);
        }

        var target = await users.GetByIdAsync(command.UserId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (target.Id == caller.UserId)
        {
            return Result<UserDto>.Fail(new Error("user.cannot_change_self", "You cannot change your own roles."));
        }

        var callerIsSysAdmin = caller.HasRole(nameof(Role.SystemAdministrator));
        var desired = new HashSet<Role>();
        foreach (var name in command.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Enum.TryParse<Role>(name, ignoreCase: true, out var role))
            {
                return Result<UserDto>.Fail(new Error("user.unknown_role", $"Unknown role: '{name}'."));
            }

            if (AdminGate.IsAdminRole(role) && !callerIsSysAdmin)
            {
                return Result<UserDto>.Fail(AuthErrors.CannotGrantRole);
            }

            if ((role == Role.Employee) != (target.UserType == UserType.Employee) &&
                (role == Role.Customer) != (target.UserType == UserType.Customer))
            {
                return Result<UserDto>.Fail(UserErrors.BaseRoleRequired);
            }

            desired.Add(role);
        }

        // Base role is structural — always present, never granted/removed here.
        desired.Add(target.UserType == UserType.Employee ? Role.Employee : Role.Customer);

        if (!callerIsSysAdmin)
        {
            foreach (var role in target.Roles.Where(AdminGate.IsAdminRole))
            {
                desired.Add(role);
            }
        }

        var now = clock.UtcNow;
        foreach (var role in desired.Except(target.Roles).ToList())
        {
            target.AddRole(role, now);
        }

        foreach (var role in target.Roles.Except(desired).ToList())
        {
            var removed = target.RemoveRole(role, now);
            if (removed.IsFailure)
            {
                return Result<UserDto>.Fail(removed.Error);
            }
        }

        await users.UpdateAsync(target, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(target, includePhone: true));
    }
}
