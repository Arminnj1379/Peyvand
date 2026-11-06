using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Sessions;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Auth;

/// <summary>Username-or-phone login. Failures never reveal whether the login exists.</summary>
public sealed record LoginCommand(
    string Login,
    string Password,
    string? DeviceLabel,
    string? IpAddress) : IRequest<Result<AuthResultDto>>;

public sealed class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Login).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
        RuleFor(x => x.DeviceLabel).MaximumLength(128);
    }
}

public sealed class LoginHandler(
    IUserStore users,
    IDeviceSessionStore sessions,
    IPasswordHasher passwords,
    IAuthTokenService tokens,
    IIdGenerator ids,
    IClock clock) : IRequestHandler<LoginCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> Handle(LoginCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var login = request.Login.Trim();

        User? user = User.IsValidUsername(User.NormalizeUsername(login))
            ? await users.GetByUsernameAsync(User.NormalizeUsername(login), ct).ConfigureAwait(false)
            : null;
        user ??= User.IsValidPhone(login)
            ? await users.GetByPhoneAsync(login, ct).ConfigureAwait(false)
            : null;

        // Constant-time-ish dummy check so unknown logins cost the same as wrong passwords.
        if (user is null)
        {
            passwords.Verify(request.Password, PasswordHasherDummy.Value);
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidCredentials);
        }

        if (!user.IsActive)
        {
            return Result<AuthResultDto>.Fail(AuthErrors.AccountInactive);
        }

        if (!passwords.Verify(request.Password, user.PasswordHash))
        {
            return Result<AuthResultDto>.Fail(AuthErrors.InvalidCredentials);
        }

        var now = clock.UtcNow;
        user.TouchSeen(now);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);

        var refreshToken = RefreshTokens.Generate();
        var session = DeviceSession.Create(
            ids.NewId(), user.Id, RefreshTokens.Hash(refreshToken),
            request.DeviceLabel, request.IpAddress, now, now + AuthLifetimes.RefreshTokenLifetime);
        await sessions.AddAsync(session, ct).ConfigureAwait(false);

        var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(user, session.Id);
        return Result<AuthResultDto>.Ok(new AuthResultDto(
            accessToken, accessExpiresAt, refreshToken, session.ExpiresAt, session.Id,
            UserMapping.ToDto(user, includePhone: true)));
    }
}

/// <summary>Well-formed PBKDF2 string so dummy verifies take the real (slow) code path.</summary>
internal static class PasswordHasherDummy
{
    internal static readonly string Value =
        "$pbkdf2-sha256$t=210000$m=AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
}
