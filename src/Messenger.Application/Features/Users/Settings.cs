using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Users;

/// <summary>User preferences: UI theme + notification master switch.</summary>
public sealed record SettingsDto(string Theme, bool NotificationsEnabled);

public sealed record GetMySettingsQuery : IRequest<Result<SettingsDto>>;

public sealed class GetMySettingsHandler(IUserStore users, ICurrentUser currentUser)
    : IRequestHandler<GetMySettingsQuery, Result<SettingsDto>>
{
    public async Task<Result<SettingsDto>> Handle(GetMySettingsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<SettingsDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<SettingsDto>.Fail(UserErrors.NotFound);
        }

        return Result<SettingsDto>.Ok(new SettingsDto(user.Theme, user.NotificationsEnabled));
    }
}

public sealed record UpdateMySettingsCommand(string Theme, bool NotificationsEnabled)
    : IRequest<Result<SettingsDto>>;

public sealed class UpdateMySettingsValidator : AbstractValidator<UpdateMySettingsCommand>
{
    public UpdateMySettingsValidator()
    {
        RuleFor(x => x.Theme).NotEmpty().Must(UserSettings.IsValidTheme)
            .WithMessage("Theme must be system, light, or dark.");
    }
}

public sealed class UpdateMySettingsHandler(
    IUserStore users, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<UpdateMySettingsCommand, Result<SettingsDto>>
{
    public async Task<Result<SettingsDto>> Handle(UpdateMySettingsCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<SettingsDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<SettingsDto>.Fail(UserErrors.NotFound);
        }

        var updated = user.UpdateSettings(request.Theme, request.NotificationsEnabled, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<SettingsDto>.Fail(updated.Error);
        }

        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<SettingsDto>.Ok(new SettingsDto(user.Theme, user.NotificationsEnabled));
    }
}
