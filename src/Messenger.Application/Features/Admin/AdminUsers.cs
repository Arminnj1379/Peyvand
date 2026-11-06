using FluentValidation;
using Messenger.Application.Auth;
using Messenger.Application.Services;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Admin;

/// <summary>
/// Administrator user management. The controller policy restricts to
/// administrators; the service re-checks (defense in depth) and enforces the
/// grant hierarchy: only a System Administrator may grant/revoke administrator
/// roles or change the active state of another administrator.
/// </summary>
public sealed record SetUserActiveCommand(string UserId, bool IsActive);

public sealed record SetUserRolesCommand(string UserId, IReadOnlyList<string> Roles);

public sealed record ListUsersQuery(
    string? UserType,
    string? Role,
    bool? IsActive,
    string? Cursor,
    int Limit);

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

internal static class AdminGate
{
    public static Result RequireAdmin(CallerContext caller) =>
        caller.HasRole(nameof(Role.SystemAdministrator)) || caller.HasRole(nameof(Role.OrganizationAdministrator))
            ? Result.Ok()
            : Result.Fail(AuthErrors.Forbidden);

    public static bool IsAdminRole(Role role) =>
        role is Role.SystemAdministrator or Role.OrganizationAdministrator;

    public static bool IsProtectedAdmin(User target) =>
        target.Roles.Any(IsAdminRole);
}
