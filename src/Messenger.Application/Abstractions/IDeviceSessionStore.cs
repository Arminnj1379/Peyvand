using Messenger.Domain.Sessions;
using Messenger.Domain.Users;

namespace Messenger.Application.Abstractions;

/// <summary>Refresh-token session store.</summary>
public interface IDeviceSessionStore
{
    Task<DeviceSession?> GetByIdAsync(string id, CancellationToken ct);

    /// <summary>Finds the session holding the hash as current <b>or</b> retired (reuse detection).</summary>
    Task<DeviceSession?> GetByAnyTokenHashAsync(string refreshTokenHash, CancellationToken ct);

    Task<IReadOnlyList<DeviceSession>> ListByUserAsync(string userId, int limit, CancellationToken ct);
    /// <summary>
    /// Atomic compare-and-swap rotation: advances the token only when the
    /// presented hash is still current. Returns false when another rotation
    /// won the race (caller must NOT treat this as theft — the client replays
    /// with its own new token). Closes the read-modify-write window of
    /// load-then-<c>UpdateAsync</c> rotation.
    /// </summary>
    Task<bool> TryRotateAsync(
        string sessionId, string expectedHash, string newHash,
        DateTime newExpiresAt, string? ipAddress, DateTime now, CancellationToken ct);
    Task AddAsync(DeviceSession session, CancellationToken ct);
    Task UpdateAsync(DeviceSession session, CancellationToken ct);
    Task RevokeAllForUserAsync(string userId, DateTime now, CancellationToken ct);
    Task RevokeAllForUserExceptAsync(string userId, string exceptSessionId, DateTime now, CancellationToken ct);
    Task DeleteAsync(string id, CancellationToken ct);
}
