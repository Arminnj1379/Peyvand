using FluentValidation;

namespace Messenger.Application.Features.Auth;

/// <summary>
/// Rotates the refresh token (single use). A retired token presented again
/// means theft: all of the victim's sessions are revoked.
/// </summary>
public sealed record RefreshSessionCommand(string RefreshToken, string? IpAddress);

public sealed class RefreshSessionValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
    }
}
