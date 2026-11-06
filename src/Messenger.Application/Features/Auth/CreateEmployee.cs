using FluentValidation;
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
    string? Phone);

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
