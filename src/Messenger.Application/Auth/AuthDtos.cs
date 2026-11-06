using Messenger.Application.Users;

namespace Messenger.Application.Auth;

/// <summary>Token set returned by register/login/refresh.</summary>
public sealed record AuthResultDto(
    string AccessToken,
    DateTime AccessExpiresAt,
    string RefreshToken,
    DateTime RefreshExpiresAt,
    string SessionId,
    UserDto User);

public sealed record SessionDto(
    string Id,
    string? DeviceLabel,
    string? IpAddress,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    bool IsCurrent,
    bool IsRevoked);
