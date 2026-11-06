using System.Text.RegularExpressions;
using Messenger.Domain.Common;

namespace Messenger.Domain.Users;

/// <summary>
/// User aggregate root. Persistence-ignorant: no BSON, no ASP.NET Core.
/// Identity and timestamps are supplied by callers (Application layer).
/// </summary>
public sealed class User : Entity
{
    private static readonly Regex UsernamePattern = new("^[a-z0-9._]{3,32}$", RegexOptions.Compiled);
    private static readonly Regex PhonePattern = new(@"^\+[1-9]\d{7,14}$", RegexOptions.Compiled);

    private readonly HashSet<Role> _roles = [];
    private readonly HashSet<string> _blockedUserIds = [];

    private User(
        string id,
        string username,
        string displayName,
        string passwordHash,
        UserType userType,
        DateTime now) : base(id)
    {
        Username = username;
        DisplayName = displayName;
        PasswordHash = passwordHash;
        UserType = userType;
        IsActive = true;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string Username { get; private set; }
    public string? Phone { get; private set; }
    public bool PhoneVerified { get; private set; }
    public UserType UserType { get; }
    public IReadOnlyCollection<Role> Roles => _roles;
    public string DisplayName { get; private set; }
    public string Bio { get; private set; } = string.Empty;
    /// <summary>UI theme preference: <c>system</c>, <c>light</c>, or <c>dark</c>.</summary>
    public string Theme { get; private set; } = UserSettings.DefaultTheme;
    /// <summary>Master switch for in-app notification surfaces (badges, toasts).</summary>
    public bool NotificationsEnabled { get; private set; } = true;    public string? AvatarObjectKey { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string PasswordHash { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<string> BlockedUserIds => _blockedUserIds;
    public DateTime? LastSeenAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    public bool IsEmployee => UserType == UserType.Employee;
    public bool HasRole(Role role) => _roles.Contains(role);

    public static Result<User> Register(
        string id,
        string username,
        string displayName,
        string passwordHash,
        UserType userType,
        IEnumerable<Role> extraRoles,
        string? phone,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var normalizedUsername = NormalizeUsername(username);
        if (!IsValidUsername(normalizedUsername))
        {
            return Result<User>.Fail(UserErrors.InvalidUsername);
        }

        if (!IsValidDisplayName(displayName))
        {
            return Result<User>.Fail(UserErrors.InvalidDisplayName);
        }

        string? normalizedPhone = null;
        if (!string.IsNullOrWhiteSpace(phone))
        {
            normalizedPhone = phone.Trim();
            if (!IsValidPhone(normalizedPhone))
            {
                return Result<User>.Fail(UserErrors.InvalidPhone);
            }
        }

        var user = new User(id, normalizedUsername, displayName.Trim(), passwordHash, userType, now);
        user._roles.Add(userType == UserType.Employee ? Role.Employee : Role.Customer);

        foreach (var role in extraRoles.Distinct())
        {
            var added = user.AddRole(role, now);
            if (added.IsFailure)
            {
                return Result<User>.Fail(added.Error);
            }
        }

        if (normalizedPhone is not null)
        {
            user.Phone = normalizedPhone;
        }

        user.Raise(new UserRegistered(user.Id, user.Username, user.UserType, now));
        return Result<User>.Ok(user);
    }

    public Result UpdateProfile(string displayName, string? bio, DateTime now)
    {
        if (!IsValidDisplayName(displayName))
        {
            return Result.Fail(UserErrors.InvalidDisplayName);
        }

        if (bio is not null && bio.Length > 255)
        {
            return Result.Fail(UserErrors.BioTooLong);
        }

        DisplayName = displayName.Trim();
        Bio = bio?.Trim() ?? string.Empty;
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result SetPasswordHash(string passwordHash, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result SetAvatar(string objectKey, string url, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        AvatarObjectKey = objectKey;
        AvatarUrl = url;
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result RemoveAvatar(DateTime now)
    {
        AvatarObjectKey = null;
        AvatarUrl = null;
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result UpdateSettings(string theme, bool notificationsEnabled, DateTime now)
    {
        if (!UserSettings.IsValidTheme(theme))
        {
            return Result.Fail(UserErrors.InvalidTheme);
        }

        Theme = theme.Trim().ToLowerInvariant();
        NotificationsEnabled = notificationsEnabled;
        UpdatedAt = now;
        return Result.Ok();
    }

    public void Activate(DateTime now)
    {
        IsActive = true;
        UpdatedAt = now;
    }

    public void Deactivate(DateTime now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public Result AddRole(Role role, DateTime now)
    {
        // Base roles are structural: the matching base role is always present
        // and is managed via UserType, not via Add/Remove.
        if ((role == Role.Employee && UserType != UserType.Employee) ||
            (role == Role.Customer && UserType != UserType.Customer))
        {
            return Result.Fail(UserErrors.BaseRoleRequired);
        }

        if (!_roles.Add(role))
        {
            return Result.Fail(UserErrors.RoleAlreadyAssigned);
        }

        UpdatedAt = now;
        return Result.Ok();
    }

    public Result RemoveRole(Role role, DateTime now)
    {
        var baseRole = UserType == UserType.Employee ? Role.Employee : Role.Customer;
        if (role == baseRole)
        {
            return Result.Fail(UserErrors.BaseRoleRequired);
        }

        _roles.Remove(role);
        UpdatedAt = now;
        return Result.Ok();
    }

    public Result Block(string targetUserId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);
        if (targetUserId == Id)
        {
            return Result.Fail(UserErrors.CannotBlockSelf);
        }

        _blockedUserIds.Add(targetUserId);
        UpdatedAt = now;
        return Result.Ok();
    }

    public void Unblock(string targetUserId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetUserId);
        _blockedUserIds.Remove(targetUserId);
        UpdatedAt = now;
    }

    public void TouchSeen(DateTime now) => LastSeenAt = now;

    /// <summary>
    /// Persistence rehydration. Bypasses <see cref="Register"/> (no events,
    /// no re-validation of already-stored state). Infrastructure use only.
    /// </summary>
    public static User Rehydrate(UserSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var user = new User(
            snapshot.Id, snapshot.Username, snapshot.DisplayName,
            snapshot.PasswordHash, snapshot.UserType, snapshot.CreatedAt);

        user._roles.Clear();
        foreach (var role in snapshot.Roles)
        {
            user._roles.Add(role);
        }

        user._blockedUserIds.Clear();
        foreach (var id in snapshot.BlockedUserIds)
        {
            user._blockedUserIds.Add(id);
        }

        user.Phone = snapshot.Phone;
        user.PhoneVerified = snapshot.PhoneVerified;
        user.Bio = snapshot.Bio;
        user.Theme = UserSettings.IsValidTheme(snapshot.Theme) ? snapshot.Theme : UserSettings.DefaultTheme;
        user.NotificationsEnabled = snapshot.NotificationsEnabled;
        user.AvatarObjectKey = snapshot.AvatarObjectKey;
        user.AvatarUrl = snapshot.AvatarUrl;
        user.IsActive = snapshot.IsActive;
        user.LastSeenAt = snapshot.LastSeenAt;
        user.UpdatedAt = snapshot.UpdatedAt;
        return user;
    }

    public static string NormalizeUsername(string username) => username.Trim().ToLowerInvariant();

    public static bool IsValidUsername(string username) => UsernamePattern.IsMatch(username);

    public static bool IsValidPhone(string phone) => PhonePattern.IsMatch(phone);

    public static bool IsValidDisplayName(string displayName) =>
        !string.IsNullOrWhiteSpace(displayName) && displayName.Trim().Length is >= 1 and <= 64;
}
