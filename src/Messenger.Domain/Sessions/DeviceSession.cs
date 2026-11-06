using Messenger.Domain.Common;

namespace Messenger.Domain.Sessions;

/// <summary>
/// Refresh-token session. Only the SHA-256 hash of the opaque token is stored;
/// the token itself never touches the database. Rotation retires the previous
/// hash (bounded list) so reuse can be detected and punished with a full revoke.
/// </summary>
public sealed class DeviceSession : Entity
{
    public const int MaxRetiredHashes = 5;

    private readonly List<string> _retiredTokenHashes = [];

    private DeviceSession(
        string id,
        string userId,
        string refreshTokenHash,
        string? deviceLabel,
        string? ipAddress,
        DateTime now,
        DateTime expiresAt) : base(id)
    {
        UserId = userId;
        RefreshTokenHash = refreshTokenHash;
        DeviceLabel = deviceLabel;
        IpAddress = ipAddress;
        CreatedAt = now;
        LastUsedAt = now;
        ExpiresAt = expiresAt;
    }

    public string UserId { get; }
    public string RefreshTokenHash { get; private set; }
    public IReadOnlyList<string> RetiredTokenHashes => _retiredTokenHashes.AsReadOnly();
    public string? DeviceLabel { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime LastUsedAt { get; private set; }
    public DateTime ExpiresAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;
    public bool IsExpired(DateTime now) => now >= ExpiresAt;
    public bool IsUsable(DateTime now) => !IsRevoked && !IsExpired(now);

    public static DeviceSession Create(
        string id,
        string userId,
        string refreshTokenHash,
        string? deviceLabel,
        string? ipAddress,
        DateTime now,
        DateTime expiresAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshTokenHash);
        if (expiresAt <= now)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAt), "Session expiry must be in the future.");
        }
        return new DeviceSession(id, userId, refreshTokenHash, deviceLabel, ipAddress, now, expiresAt);
    }

    /// <summary>True when the presented hash is a previously rotated-out token (possible theft).</summary>
    public bool IsRetired(string refreshTokenHash) => _retiredTokenHashes.Contains(refreshTokenHash);

    public void Rotate(string newRefreshTokenHash, DateTime newExpiresAt, string? ipAddress, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newRefreshTokenHash);
        _retiredTokenHashes.Add(RefreshTokenHash);
        if (_retiredTokenHashes.Count > MaxRetiredHashes)
        {
            _retiredTokenHashes.RemoveAt(0);
        }

        RefreshTokenHash = newRefreshTokenHash;
        ExpiresAt = newExpiresAt;
        LastUsedAt = now;
        IpAddress = ipAddress;
    }

    public void Touch(string? ipAddress, DateTime now)
    {
        LastUsedAt = now;
        IpAddress = ipAddress;
    }

    public void Revoke(DateTime now)
    {
        RevokedAt ??= now;
    }

    /// <summary>
    /// Persistence rehydration. Bypasses <see cref="Create"/> (no re-validation
    /// of already-stored state). Infrastructure use only.
    /// </summary>
    public static DeviceSession Rehydrate(SessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var session = new DeviceSession(
            snapshot.Id, snapshot.UserId, snapshot.RefreshTokenHash, snapshot.DeviceLabel,
            snapshot.IpAddress, snapshot.CreatedAt, snapshot.ExpiresAt);
        session._retiredTokenHashes.AddRange(snapshot.RetiredTokenHashes);
        session.LastUsedAt = snapshot.LastUsedAt;
        session.RevokedAt = snapshot.RevokedAt;
        return session;
    }
}
