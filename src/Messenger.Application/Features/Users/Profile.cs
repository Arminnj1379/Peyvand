using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Users;

public sealed record UpdateMyProfileCommand(string DisplayName, string? Bio) : IRequest<Result<UserDto>>;

public sealed class UpdateMyProfileValidator : AbstractValidator<UpdateMyProfileCommand>
{
    public UpdateMyProfileValidator()
    {
        RuleFor(x => x.DisplayName).NotEmpty().MinimumLength(1).MaximumLength(64);
        RuleFor(x => x.Bio).MaximumLength(255);
    }
}

public sealed class UpdateMyProfileHandler(IUserStore users, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<UpdateMyProfileCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(UpdateMyProfileCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        var updated = user.UpdateProfile(request.DisplayName, request.Bio, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<UserDto>.Fail(updated.Error);
        }

        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }
}

public sealed record BlockUserCommand(string TargetUserId) : IRequest<Result>;

public sealed record UnblockUserCommand(string TargetUserId) : IRequest<Result>;

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

public sealed class BlockUserHandler(IUserStore users, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<BlockUserCommand, Result>
{
    public async Task<Result> Handle(BlockUserCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Fail(UserErrors.NotFound);
        }

        var target = await users.GetByIdAsync(request.TargetUserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive)
        {
            return Result.Fail(UserErrors.NotFound);
        }

        var blocked = user.Block(target.Id, clock.UtcNow);
        if (blocked.IsFailure)
        {
            return blocked;
        }

        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}

public sealed class UnblockUserHandler(IUserStore users, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<UnblockUserCommand, Result>
{
    public async Task<Result> Handle(UnblockUserCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result.Fail(UserErrors.NotFound);
        }

        user.Unblock(request.TargetUserId, clock.UtcNow);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
