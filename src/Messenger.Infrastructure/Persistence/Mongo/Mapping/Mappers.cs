using Messenger.Domain.Sessions;
using Messenger.Domain.Users;
using Messenger.Infrastructure.Persistence.Mongo.Documents;

namespace Messenger.Infrastructure.Persistence.Mongo.Mapping;

/// <summary>Document ↔ aggregate translation. The only place that knows both shapes.</summary>
internal static class UserMapper
{
    public static UserDocument ToDocument(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Phone = user.Phone,
        PhoneVerified = user.PhoneVerified,
        UserType = user.UserType,
        Roles = user.Roles.Select(r => r.ToString()).ToList(),
        DisplayName = user.DisplayName,
        Bio = user.Bio,
        Theme = user.Theme,
        NotificationsEnabled = user.NotificationsEnabled,
        AvatarObjectKey = user.AvatarObjectKey,
        AvatarUrl = user.AvatarUrl,
        PasswordHash = user.PasswordHash,
        IsActive = user.IsActive,
        BlockedUserIds = user.BlockedUserIds.ToList(),
        LastSeenAt = user.LastSeenAt,
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt
    };

    public static User ToDomain(UserDocument doc) => User.Rehydrate(new UserSnapshot(
        doc.Id,
        doc.Username,
        doc.Phone,
        doc.PhoneVerified,
        doc.UserType,
        doc.Roles
            .Select(r => Enum.TryParse<Role>(r, out var role) ? role : (Role?)null)
            .Where(r => r.HasValue)
            .Select(r => r!.Value)
            .ToList(),
        doc.DisplayName,
        doc.Bio,
        doc.Theme,
        doc.NotificationsEnabled,
        doc.AvatarObjectKey,
        doc.AvatarUrl,
        doc.PasswordHash,
        doc.IsActive,
        doc.BlockedUserIds,
        doc.LastSeenAt,
        doc.CreatedAt,
        doc.UpdatedAt));
}

/// <summary>Document ↔ aggregate translation for refresh-token sessions.</summary>
internal static class SessionMapper
{
    public static DeviceSessionDocument ToDocument(DeviceSession session) => new()
    {
        Id = session.Id,
        UserId = session.UserId,
        RefreshTokenHash = session.RefreshTokenHash,
        RetiredTokenHashes = session.RetiredTokenHashes.ToList(),
        DeviceLabel = session.DeviceLabel,
        IpAddress = session.IpAddress,
        CreatedAt = session.CreatedAt,
        LastUsedAt = session.LastUsedAt,
        ExpiresAt = session.ExpiresAt,
        RevokedAt = session.RevokedAt
    };

    public static DeviceSession ToDomain(DeviceSessionDocument doc) => DeviceSession.Rehydrate(new SessionSnapshot(
        doc.Id,
        doc.UserId,
        doc.RefreshTokenHash,
        doc.RetiredTokenHashes,
        doc.DeviceLabel,
        doc.IpAddress,
        doc.CreatedAt,
        doc.LastUsedAt,
        doc.ExpiresAt,
        doc.RevokedAt));
}
