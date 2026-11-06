using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Auth;

/// <summary>Password change. All other sessions are revoked; the current one survives.</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result>;

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must differ from the current one.");
    }
}

public sealed class ChangePasswordHandler(
    IUserStore users,
    IDeviceSessionStore sessions,
    IPasswordHasher passwords,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<ChangePasswordCommand, Result>
{
    public async Task<Result> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var now = clock.UtcNow;
        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Fail(AuthErrors.AccountInactive);
        }

        if (!passwords.Verify(request.CurrentPassword, user.PasswordHash))
        {
            return Result.Fail(AuthErrors.InvalidCredentials);
        }

        user.SetPasswordHash(passwords.Hash(request.NewPassword), now);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);

        if (currentUser.SessionId is null)
        {
            await sessions.RevokeAllForUserAsync(user.Id, now, ct).ConfigureAwait(false);
        }
        else
        {
            await sessions.RevokeAllForUserExceptAsync(user.Id, currentUser.SessionId, now, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }
}
