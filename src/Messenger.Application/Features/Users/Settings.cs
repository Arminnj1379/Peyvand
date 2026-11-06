using FluentValidation;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Users;

/// <summary>User preferences: UI theme + notification master switch.</summary>
public sealed record SettingsDto(string Theme, bool NotificationsEnabled);

public sealed record GetMySettingsQuery();

public sealed record UpdateMySettingsCommand(string Theme, bool NotificationsEnabled);

public sealed class UpdateMySettingsValidator : AbstractValidator<UpdateMySettingsCommand>
{
    public UpdateMySettingsValidator()
    {
        RuleFor(x => x.Theme).NotEmpty().Must(UserSettings.IsValidTheme)
            .WithMessage("Theme must be system, light, or dark.");
    }
}
