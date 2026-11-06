using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Sessions;
using Messenger.Domain.Users;

namespace Messenger.Application.Auth;

/// <summary>External customer self-registration. Always creates an active Customer and signs them in.</summary>
public sealed record RegisterCustomerCommand(
    string Username,
    string DisplayName,
    string Password,
    string? Phone,
    string? DeviceLabel,
    string? IpAddress) : IRequest<Result<AuthResultDto>>;

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

public sealed class RegisterCustomerHandler(
    IUserStore users,
    IDeviceSessionStore sessions,
    IPasswordHasher passwords,
    IAuthTokenService tokens,
    IIdGenerator ids,
    IClock clock) : IRequestHandler<RegisterCustomerCommand, Result<AuthResultDto>>
{
    public async Task<Result<AuthResultDto>> Handle(RegisterCustomerCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var now = clock.UtcNow;
        var username = User.NormalizeUsername(request.Username);

        if (await users.ExistsByUsernameAsync(username, ct).ConfigureAwait(false))
        {
            return Result<AuthResultDto>.Fail(UserErrors.UsernameTaken);
        }

        var phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim();
        if (phone is not null && await users.ExistsByPhoneAsync(phone, ct).ConfigureAwait(false))
        {
            return Result<AuthResultDto>.Fail(UserErrors.PhoneTaken);
        }

        var registered = User.Register(
            ids.NewId(), username, request.DisplayName, passwords.Hash(request.Password),
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
            request.DeviceLabel, request.IpAddress, now, now + AuthLifetimes.RefreshTokenLifetime);
        await sessions.AddAsync(session, ct).ConfigureAwait(false);

        var (accessToken, accessExpiresAt) = tokens.CreateAccessToken(user, session.Id);
        return Result<AuthResultDto>.Ok(new AuthResultDto(
            accessToken, accessExpiresAt, refreshToken, session.ExpiresAt, session.Id,
            UserMapping.ToDto(user, includePhone: true)));
    }
}
