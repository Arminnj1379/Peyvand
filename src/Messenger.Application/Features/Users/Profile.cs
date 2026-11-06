using FluentValidation;

namespace Messenger.Application.Features.Users;

public sealed record UpdateMyProfileCommand(string DisplayName, string? Bio);

public sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(1).MaximumLength(64);
        RuleFor(x => x.Bio).MaximumLength(255);
    }
}

public sealed record BlockUserCommand(string TargetUserId);

public sealed record UnblockUserCommand(string TargetUserId);

public sealed class BlockUserValidator : AbstractValidator<BlockUserCommand>
{
    public BlockUserValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class UnblockUserValidator : AbstractValidator<UnblockUserCommand>
{
    public UnblockUserValidator()
    {
        RuleFor(x => x.TargetUserId).NotEmpty().MaximumLength(64);
    }
}
