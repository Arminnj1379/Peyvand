namespace Messenger.Domain.Sessions;

/// <summary>Full stored state of a <see cref="DeviceSession"/> for rehydration. Infrastructure use only.</summary>
public sealed record SessionSnapshot(
    string Id,
    string UserId,
    string RefreshTokenHash,
    IReadOnlyList<string> RetiredTokenHashes,
    string? DeviceLabel,
    string? IpAddress,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    DateTime ExpiresAt,
    DateTime? RevokedAt);
