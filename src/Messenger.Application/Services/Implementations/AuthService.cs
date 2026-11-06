using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Auth;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Sessions;
using Messenger.Domain.Users;

namespace Messenger.Application.Services.Implementations;

/// <summary>
/// Authentication use cases. Logic moved verbatim from the former MediatR
/// handlers; validation runs inline (same validators, same
/// <see cref="ValidationException"/> shape the API middleware maps to 400).
/// Validators are stateless and co-located with their commands, so services
/// instantiate them directly instead of growing a validator per constructor
/// parameter.
/// </summary>
public sealed class AuthService(
    IUserStore users,
    IDeviceSessionStore sessions,
    IPasswordHasher passwords,
    IAuthTokenService tokens,
    IIdGenerator ids,
    IClock clock) : IAuthService
{
    public async Task<Result<AuthResultDto>> RegisterAsync(RegisterCustomerCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await new RegisterCustomerValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        var now = clock.UtcNow;
        var username = User.NormalizeUsername(command.Username);

        if (await users.ExistsByUsernameAsync(username, ct).ConfigureAwait(false))
        {
            return Result<AuthResultDto>.Fail(UserErrors.UsernameTaken);
        }

        var phone = string.IsNullOrWhiteSpace(command.Phone) ? null : command.Phone.Trim();
        if (phone is not null && await users.ExistsByPhoneAsync(phone, ct).ConfigureAwait(false))
        {
            return Result<AuthResultDto>.Fail(UserErrors.PhoneTaken);
        }

        var registered = User.Register(
            ids.NewId(), username, command.DisplayName, passwords.Hash(command.Password),
            UserType.Customer, [], phone, now);
        if (registered.IsFailure)
        {
            return Result<AuthResultDto>.Fail(registered.Error);
        }

        var user = registered.Value!;
        try
        {
            await users.AddAsync(user, ct).ConfigureAwait(false);
        }
        catch (DuplicateUserException ex)
        {
            // Lost the uniqueness race despite the pre-checks: surface the
            // conflicting claim (409) through the normal result path instead
            // of the middleware backstop, keeping one error shape.
            return Result<AuthResultDto>.Fail(ex.Error);
        }

        var refreshToken = RefreshTokens.Generate();
        var session = DeviceSession.Create(
            ids.NewId(), user.Id, RefreshTokens.Hash(refreshToken),
            command.DeviceLabel, command.IpAddress, now, now + AuthLifetimes.RefreshTokenLifetime);
        await sessions.AddAsync(session, ct).ConfigureAwait(false);

        var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(user, session.Id);
        return Result<AuthResultDto>.Ok(new AuthResultDto(
            accessToken, accessExpiresAt, refreshToken, session.ExpiresAt, session.Id,
            UserMapping.ToDto(user, includePhone: true)));
    }

    public async Task<Result<AuthResultDto>> LoginAsync(LoginCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await new LoginValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        var login = command.Login.Trim();

        User? user = User.IsValidUsername(User.NormalizeUsername(login))
            ? await users.GetByUsernameAsync(User.NormalizeUsername(login), ct).ConfigureAwait(false)
            : null;
        user ??= User.IsValidPhone(login)
            ? await users.GetByPhoneAsync(login, ct).ConfigureAwait(false)
            : null;

        // Constant-time-ish dummy check so unknown logins cost the same as wrong passwords.
        if (user is null)
        {
            passwords.Verify(command.Password, PasswordHasherDummy.Value);
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return Result<AuthResultDto>.Fail(AuthErrors.AccountInactive);
        }

        if (!passwords.Verify(command.Password, user.PasswordHash))
        {
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidCredentials);
        }

        var now = clock.UtcNow;
        user.TouchSeen(now);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);

        var refreshToken = RefreshTokens.Generate();
        var session = DeviceSession.Create(
            ids.NewId(), user.Id, RefreshTokens.Hash(refreshToken),
            command.DeviceLabel, command.IpAddress, now, now + AuthLifetimes.RefreshTokenLifetime);
        await sessions.AddAsync(session, ct).ConfigureAwait(false);

        var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(user, session.Id);
        return Result<AuthResultDto>.Ok(new AuthResultDto(
            accessToken, accessExpiresAt, refreshToken, session.ExpiresAt, session.Id,
            UserMapping.ToDto(user, includePhone: true)));
    }

    public async Task<Result<AuthResultDto>> RefreshAsync(RefreshSessionCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await new RefreshSessionValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        var now = clock.UtcNow;
        var hash = RefreshTokens.Hash(command.RefreshToken);

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
            nextExpiresAt, command.IpAddress, now, ct).ConfigureAwait(false);
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

    public async Task<Result> LogoutAsync(CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.SessionId is null || caller.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var session = await sessions.GetByIdAsync(caller.SessionId, ct).ConfigureAwait(false);
        if (session is not null && session.UserId == caller.UserId)
        {
            session.Revoke(clock.UtcNow);
            await sessions.UpdateAsync(session, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }

    public async Task<Result> LogoutAllAsync(CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        await sessions.RevokeAllForUserAsync(caller.UserId, clock.UtcNow, ct).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result<IReadOnlyList<SessionDto>>> ListSessionsAsync(CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(caller);
        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<SessionDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var all = await sessions.ListByUserAsync(caller.UserId, 100, ct).ConfigureAwait(false);
        var dtos = all
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(
                s.Id, s.DeviceLabel, s.IpAddress, s.CreatedAt, s.LastUsedAt,
                s.ExpiresAt, s.Id == caller.SessionId, s.IsRevoked))
            .ToList();
        return Result<IReadOnlyList<SessionDto>>.Ok(dtos);
    }

    public async Task<Result> RevokeSessionAsync(RevokeSessionCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new RevokeSessionValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var session = await sessions.GetByIdAsync(command.SessionId, ct).ConfigureAwait(false);
        if (session is null || session.UserId != caller.UserId)
        {
            return Result.Fail(UserErrors.SessionNotFound);
        }

        session.Revoke(clock.UtcNow);
        await sessions.UpdateAsync(session, ct).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result> ChangePasswordAsync(ChangePasswordCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new ChangePasswordValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var now = clock.UtcNow;
        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Fail(AuthErrors.AccountInactive);
        }

        if (!passwords.Verify(command.CurrentPassword, user.PasswordHash))
        {
            return Result.Fail(AuthErrors.InvalidCredentials);
        }

        user.SetPasswordHash(passwords.Hash(command.NewPassword), now);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);

        if (caller.SessionId is null)
        {
            await sessions.RevokeAllForUserAsync(user.Id, now, ct).ConfigureAwait(false);
        }
        else
        {
            await sessions.RevokeAllForUserExceptAsync(user.Id, caller.SessionId, now, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }
}
