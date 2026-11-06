using Messenger.Domain.Users;

namespace Messenger.Application.Users;

/// <summary>Public user projection. Phone is only populated for self/admin views.</summary>
public sealed record UserDto(
    string Id,
    string Username,
    string DisplayName,
    string Bio,
    string? AvatarUrl,
    string UserType,
    IReadOnlyList<string> Roles,
    bool IsActive,
    string? Phone,
    DateTime? LastSeenAt);

public static class UserMapping
{
    public static UserDto ToDto(User user, bool includePhone)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserDto(
            user.Id,
            user.Username,
            user.DisplayName,
            user.Bio,
            user.AvatarUrl,
            user.UserType.ToString(),
            user.Roles.Select(r => r.ToString()).OrderBy(r => r).ToList(),
            user.IsActive,
            includePhone ? user.Phone : null,
            user.LastSeenAt);
    }
}
