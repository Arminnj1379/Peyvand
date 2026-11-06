using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Admin;

/// <summary>
/// Administrator user management. Endpoint policy restricts to administrators;
/// handlers re-check (defense in depth) and enforce the grant hierarchy:
/// only a System Administrator may grant/revoke administrator roles or
/// change the active state of another administrator.
/// </summary>
public sealed record SetUserActiveCommand(string UserId, bool IsActive) : IRequest<Result<UserDto>>;

public sealed record SetUserRolesCommand(string UserId, IReadOnlyList<string> Roles)
    : IRequest<Result<UserDto>>;

public sealed record ListUsersQuery(
    string? UserType,
    string? Role,
    bool? IsActive,
    string? Cursor,
    int Limit) : IRequest<Result<IReadOnlyList<UserDto>>>;

public sealed class SetUserActiveValidator : AbstractValidator<SetUserActiveCommand>
{
    public SetUserActiveValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class SetUserRolesValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Roles).NotNull();
        RuleForEach(x => x.Roles).Must(r => Enum.TryParse<Role>(r, ignoreCase: true, out _))
            .WithMessage("Unknown role: '{PropertyValue}'.");
    }
}

public sealed class ListUsersValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
        RuleFor(x => x.Cursor).MaximumLength(64);
    }
}

public sealed class SetUserActiveHandler(
    IUserStore users,
    IDeviceSessionStore sessions,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SetUserActiveCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(SetUserActiveCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gate = AdminGate.RequireAdmin(currentUser);
        if (gate.IsFailure)
        {
            return Result<UserDto>.Fail(gate.Error);
        }

        var target = await users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (target.Id == currentUser.UserId)
        {
            return Result<UserDto>.Fail(new Error("user.cannot_change_self", "You cannot change your own active state."));
        }

        if (AdminGate.IsProtectedAdmin(target) &&
            !currentUser.HasRole(nameof(Role.SystemAdministrator)))
        {
            return Result<UserDto>.Fail(AuthErrors.CannotGrantRole);
        }

        var now = clock.UtcNow;
        if (request.IsActive)
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
}

public sealed class SetUserRolesHandler(
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SetUserRolesCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(SetUserRolesCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gate = AdminGate.RequireAdmin(currentUser);
        if (gate.IsFailure)
        {
            return Result<UserDto>.Fail(gate.Error);
        }

        var target = await users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (target is null)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (target.Id == currentUser.UserId)
        {
            return Result<UserDto>.Fail(new Error("user.cannot_change_self", "You cannot change your own roles."));
        }

        var callerIsSysAdmin = currentUser.HasRole(nameof(Role.SystemAdministrator));
        var desired = new HashSet<Role>();
        foreach (var name in request.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
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

public sealed class ListUsersHandler(IUserStore users, ICurrentUser currentUser)
    : IRequestHandler<ListUsersQuery, Result<IReadOnlyList<UserDto>>>
{
    public async Task<Result<IReadOnlyList<UserDto>>> Handle(ListUsersQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var gate = AdminGate.RequireAdmin(currentUser);
        if (gate.IsFailure)
        {
            return Result<IReadOnlyList<UserDto>>.Fail(gate.Error);
        }

        var found = await users.ListAsync(request.UserType, request.Role, request.IsActive, request.Cursor, request.Limit, ct)
            .ConfigureAwait(false);
        return Result<IReadOnlyList<UserDto>>.Ok(
            found.Select(u => UserMapping.ToDto(u, includePhone: true)).ToList());
    }
}

internal static class AdminGate
{
    public static Result RequireAdmin(ICurrentUser caller) =>
        caller.HasRole(nameof(Role.SystemAdministrator)) || caller.HasRole(nameof(Role.OrganizationAdministrator))
            ? Result.Ok()
            : Result.Fail(AuthErrors.Forbidden);

    public static bool IsAdminRole(Role role) =>
        role is Role.SystemAdministrator or Role.OrganizationAdministrator;

    public static bool IsProtectedAdmin(User target) =>
        target.Roles.Any(IsAdminRole);
}
