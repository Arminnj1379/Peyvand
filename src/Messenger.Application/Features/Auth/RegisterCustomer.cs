using FluentValidation;
using Messenger.Domain.Users;

namespace Messenger.Application.Auth;

/// <summary>External customer self-registration. Always creates an active Customer and signs them in.</summary>
public sealed record RegisterCustomerCommand(
    string Username,
    string DisplayName,
    string Password,
    string? Phone,
    string? DeviceLabel,
    string? IpAddress);

public sealed class RegisterCustomerValidator : AbstractValidator<RegisterCustomerCommand>
{
    public RegisterCustomerValidator()
    {
        RuleFor(x => x.Username).NotEmpty().MinimumLength(3).MaximumLength(32);
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(1).MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8).MaximumLength(128);
        RuleFor(x => x.Phone).Must(p => p is null || User.IsValidPhone(p.Trim()))
            .WithMessage("Phone number must be in E.164 format (e.g. +989121234567).");
        RuleFor(x => x.DeviceLabel).MaximumLength(128);
    }
}
