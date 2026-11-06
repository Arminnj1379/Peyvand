using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Auth;

/// <summary>
/// Rotates the refresh token (single use). A retired token presented again
/// means theft: all of the victim's sessions are revoked.
/// </summary>
public sealed record RefreshSessionCommand(string RefreshToken, string? IpAddress)
    : IRequest<Result<AuthResultDto>>;

public sealed class RefreshSessionValidator : AbstractValidator<RefreshSessionCommand>
{
    public RefreshSessionValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(256);
    }
}

public sealed class RefreshSessionHandler(
    IUserStore users,
    IDeviceSessionStore sessions,
    IAuthTokenService tokens,
    IClock clock) : IRequestHandler<RefreshSessionCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> Handle(RefreshSessionCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var hash = RefreshTokens.Hash(request.RefreshToken);

        var session = await sessions.GetByAnyTokenHashAsync(hash, ct).ConfigureAwait(false);
        if (session is null)
        {
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidRefreshToken);
        }

        if (session.IsRetired(hash))
        {
            // Reuse of a rotated-out token: assume theft, nuke everything.
            await sessions.RevokeAllForUserAsync(session.UserId, now, ct).ConfigureAwait(false);
            return Result<AuthResultDto>.Fail(AuthErrors.ReuseDetected);
        }

        if (session.IsRevoked)
        {
            return Result<AuthResultDto>.Fail(AuthErrors.SessionRevoked);
        }

        if (session.IsExpired(now))
        {
            await sessions.DeleteAsync(session.Id, ct).ConfigureAwait(false);
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidRefreshToken);
        }

        var user = await users.GetByIdAsync(session.UserId, ct).ConfigureAwait(false);
        if (user is null)
        {
            await sessions.DeleteAsync(session.Id, ct).ConfigureAwait(false);
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidRefreshToken);
        }

        if (!user.IsActive)
        {
            session.Revoke(now);
            await sessions.UpdateAsync(session, ct).ConfigureAwait(false);
            return Result<AuthResultDto>.Fail(AuthErrors.AccountInactive);
        }

        var nextRefreshToken = RefreshTokens.Generate();
        var nextExpiresAt = now + AuthLifetimes.RefreshTokenLifetime;
        var rotated = await sessions.TryRotateAsync(
            session.Id, hash, RefreshTokens.Hash(nextRefreshToken),
            nextExpiresAt, request.IpAddress, now, ct).ConfigureAwait(false);
        if (!rotated)
        {
            // A concurrent rotation (or revoke) won the race: our token is no
            // longer current. The client replays with its own fresh token —
            // this is contention, not theft, so no full revoke here.
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidRefreshToken);
        }

        user.TouchSeen(now);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);

        var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(user, session.Id);
        return Result<AuthResultDto>.Ok(new AuthResultDto(
            accessToken, accessExpiresAt, nextRefreshToken, nextExpiresAt, session.Id,
            UserMapping.ToDto(user, includePhone: true)));
    }
}
