using FluentValidation;

namespace Messenger.Application.Features.Auth;

/// <summary>Session management for the current caller (from the JWT sid claim).</summary>
public sealed record LogoutCommand();

public sealed record LogoutAllCommand();

public sealed record ListSessionsQuery();

public sealed record RevokeSessionCommand(string SessionId);

public sealed class RevokeSessionValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().MaximumLength(64);
    }
}
