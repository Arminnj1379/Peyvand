using Messenger.Domain.Common;

namespace Messenger.Domain.Users;

/// <summary>Machine-readable user/session failures. Messages are safe to surface.</summary>
public static class UserErrors
{
    public static readonly Error InvalidUsername =
        new("user.invalid_username", "Username must be 3-32 characters: lowercase letters, digits, '.' or '_'.");

    public static readonly Error InvalidPhone =
        new("user.invalid_phone", "Phone number must be in E.164 format (e.g. +989121234567).");

    public static readonly Error InvalidDisplayName =
        new("user.invalid_display_name", "Display name must be 1-64 characters.");

    public static readonly Error BioTooLong =
        new("user.bio_too_long", "Bio must be 255 characters or fewer.");

    public static readonly Error InvalidTheme =
        new("user.invalid_theme", "Theme must be system, light, or dark.");

    public static readonly Error UsernameTaken =
        new("user.username_taken", "This username is already taken.");

    public static readonly Error PhoneTaken =
        new("user.phone_taken", "This phone number is already registered.");

    public static readonly Error NotFound =
        new("user.not_found", "User not found.");

    public static readonly Error Inactive =
        new("user.inactive", "This account is deactivated.");

    public static readonly Error CannotBlockSelf =
        new("user.cannot_block_self", "You cannot block yourself.");

    public static readonly Error BaseRoleRequired =
        new("user.base_role_required", "An account must always keep its Employee or Customer role.");

    public static readonly Error RoleAlreadyAssigned =
        new("user.role_conflict", "Role is already assigned.");

    public static readonly Error SessionNotFound =
        new("session.not_found", "Session not found.");

    public static readonly Error SessionRevoked =
        new("session.revoked", "Session has been revoked.");

    public static readonly Error AvatarTooLarge =
        new("user.avatar_too_large", "Avatar must be 2 MB or smaller.");

    public static readonly Error AvatarUnsupportedType =
        new("user.avatar_unsupported_type", "Avatar must be a JPEG, PNG, or WebP image.");
}
