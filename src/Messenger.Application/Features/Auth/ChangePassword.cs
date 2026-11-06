using FluentValidation;

namespace Messenger.Application.Features.Auth;

/// <summary>Password change. All other sessions are revoked; the current one survives.</summary>
public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword);

public sealed class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(128);
        RuleFor(x => x.NewPassword).NotEmpty().MinimumLength(8).MaximumLength(128)
            .NotEqual(x => x.CurrentPassword).WithMessage("The new password must differ from the current one.");
    }
}
