namespace Messenger.Domain.Users;

/// <summary>
/// User preference values. Theme is a closed set (system/light/dark);
/// notifications is a master switch layered over per-chat mute flags.
/// </summary>
public static class UserSettings
{
    public const string DefaultTheme = "system";

    public static bool IsValidTheme(string? theme) =>
        theme is not null && theme.Trim().ToLowerInvariant() is "system" or "light" or "dark";
}
