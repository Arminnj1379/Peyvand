namespace Messenger.Domain.Users;

/// <summary>Full stored state of a <see cref="User"/> for rehydration. Infrastructure use only.</summary>
public sealed record UserSnapshot(
    string Id,
    string Username,
    string? Phone,
    bool PhoneVerified,
    UserType UserType,
    IReadOnlyList<Role> Roles,
    string DisplayName,
    string Bio,
    string Theme,
    bool NotificationsEnabled,
    string? AvatarObjectKey,
    string? AvatarUrl,
    string PasswordHash,
    bool IsActive,
    IReadOnlyList<string> BlockedUserIds,
    DateTime? LastSeenAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
