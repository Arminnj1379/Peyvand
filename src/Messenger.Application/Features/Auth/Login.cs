using FluentValidation;

namespace Messenger.Application.Features.Auth;

/// <summary>Username-or-phone login. Failures never reveal whether the login exists.</summary>
public sealed record LoginCommand(
    string Login,
    string Password,
    string? DeviceLabel,
    string? IpAddress);

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Login).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DeviceLabel).MaximumLength(128);
    }
}

/// <summary>Well-formed PBKDF2 string so dummy verifies take the real (slow) code path.</summary>
internal static class PasswordHasherDummy
{
    internal static readonly string Value =
        "$pbkdf2-sha256$t=210000$m=AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}
