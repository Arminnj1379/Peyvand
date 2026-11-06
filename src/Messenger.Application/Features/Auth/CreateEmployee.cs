using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Auth;

/// <summary>
/// Internal employee creation. Caller must be an administrator (enforced by the
/// endpoint policy AND re-checked here). Organization administrators cannot
/// grant administrator roles — only a System Administrator can.
/// </summary>
public sealed record CreateEmployeeCommand(
    string Username,
    string DisplayName,
    string Password,
    IReadOnlyList<string> Roles,
    string? Phone) : IRequest<Result<UserDto>>;

public sealed class CreateEmployeeValidator : AbstractValidator<CreateEmployeeCommand>
{
    public CreateEmployeeValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(32);
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(1).MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Roles).NotNull();
        RuleForEach(x => x.Roles).Must(r => Enum.TryParse<Role>(r, ignoreCase: true, out _))
            .WithMessage("Unknown role: '{PropertyValue}'.");
        RuleFor(x => x.Phone).Must(p => p is null || User.IsValidPhone(p.Trim()))
            .WithMessage("Phone number must be in E.164 format (e.g. +989121234567).");
    }
}

public sealed class CreateEmployeeHandler(
    IUserStore users,
    IPasswordHasher passwords,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<CreateEmployeeCommand, Result<UserDto>>
{
    private static readonly Role[] AdminOnlyRoles = [Role.SystemAdministrator, Role.OrganizationAdministrator];

    public async Task<Result<UserDto>> Handle(CreateEmployeeCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var callerIsSysAdmin = currentUser.HasRole(nameof(Role.SystemAdministrator));
        var callerIsOrgAdmin = currentUser.HasRole(nameof(Role.OrganizationAdministrator));
        if (!callerIsSysAdmin && !callerIsOrgAdmin)
        {
            return Result<UserDto>.Fail(AuthErrors.Forbidden);
        }

        var roles = new List<Role>();
        foreach (var name in request.Roles.Distinct(StringComparer.OrdinalIgnoreCase))
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
        var username = User.NormalizeUsername(request.Username);
        if (await users.ExistsByUsernameAsync(username, ct).ConfigureAwait(false))
        {
            return Result<UserDto>.Fail(UserErrors.UsernameTaken);
        }

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is not null && await users.ExistsByPhoneAsync(phone, ct).ConfigureAwait(false))
        {
            return Result<UserDto>.Fail(UserErrors.PhoneTaken);
        }

        var registered = User.Register(
            ids.NewId(), username, request.DisplayName, passwords.Hash(request.Password),
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
}
