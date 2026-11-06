using Messenger.Application.Abstractions;
using Messenger.Domain.Sessions;
using Messenger.Domain.Users;

namespace Messenger.Application.Tests;

/// <summary>In-memory ports for handler tests. No Mongo, no ASP.NET Core.</summary>
internal sealed class FakeClock(DateTime now) : IClock
{
    public DateTime Now { get; set; } = now;
    public DateTime UtcNow => Now;
}

internal sealed class FakeIds : IIdGenerator
{
    private int _counter;
    public string NewId() => $"id-{++_counter}";
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => $"H:{password}";
    public bool Verify(string password, string passwordHash) => passwordHash == $"H:{password}";
}

internal sealed class FakeTokenService(DateTime now) : IAuthTokenService
{
    public (string Token, DateTime ExpiresAt) CreateAccessToken(User user, string sessionId) =>
        ($"access-{user.Id}-{sessionId}", now.AddMinutes(15));

    public string CreateRefreshToken() => Auth.RefreshTokens.Generate();
}

internal sealed class FakeUserStore : IUserStore
{
    private readonly Dictionary<string, User> _users = [];

    /// <summary>Simulates losing the username/phone uniqueness race on insert.</summary>
    public bool ThrowDuplicateOnAdd { get; set; }

    public void Seed(User user) => _users[user.Id] = user;

    public Task<User?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_users.TryGetValue(id, out var u) ? u : null);

    public Task<User?> GetByUsernameAsync(string username, CancellationToken ct) =>
        Task.FromResult(_users.Values.FirstOrDefault(u => u.Username == username));

    public Task<User?> GetByPhoneAsync(string phone, CancellationToken ct) =>
        Task.FromResult(_users.Values.FirstOrDefault(u => u.Phone == phone));

    public Task<bool> ExistsByUsernameAsync(string username, CancellationToken ct) =>
        Task.FromResult(_users.Values.Any(u => u.Username == username));

    public Task<bool> ExistsByPhoneAsync(string phone, CancellationToken ct) =>
        Task.FromResult(_users.Values.Any(u => u.Phone == phone));

    public Task<bool> AnyAdministratorAsync(CancellationToken ct) =>
        Task.FromResult(_users.Values.Any(u => u.HasRole(Role.SystemAdministrator)));

    public Task AddAsync(User user, CancellationToken ct)
    {
        if (ThrowDuplicateOnAdd)
        {
            throw new DuplicateUserException(Domain.Users.UserErrors.UsernameTaken);
        }

        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(User user, CancellationToken ct)
    {
        _users[user.Id] = user;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<User>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<User>>(_users.Values
            .Where(u => ids.Contains(u.Id))
            .ToList());

    public Task<IReadOnlyList<User>> SearchAsync(string query, bool includeCustomers, int limit, CancellationToken ct)
    {
        var found = _users.Values
            .Where(u => u.Username.Contains(query, StringComparison.OrdinalIgnoreCase)
                || u.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Where(u => includeCustomers || u.UserType == UserType.Employee)
            .Take(limit)
            .ToList();
        return Task.FromResult<IReadOnlyList<User>>(found);
    }

    public Task<IReadOnlyList<User>> ListAsync(
        string? userType, string? role, bool? isActive, string? cursor, int limit, CancellationToken ct)
    {
        IEnumerable<User> query = _users.Values;
        if (Enum.TryParse<UserType>(userType, ignoreCase: true, out var t))
        {
            query = query.Where(u => u.UserType == t);
        }

        if (Enum.TryParse<Role>(role, ignoreCase: true, out var r))
        {
            query = query.Where(u => u.HasRole(r));
        }

        if (isActive.HasValue)
        {
            query = query.Where(u => u.IsActive == isActive.Value);
        }

        return Task.FromResult<IReadOnlyList<User>>(query.Take(limit).ToList());
    }
}

internal sealed class FakeSessionStore : IDeviceSessionStore
{
    private readonly Dictionary<string, DeviceSession> _sessions = [];

    /// <summary>Simulates losing the rotation race (another rotation committed first).</summary>
    public bool FailRotate { get; set; }

    public Task<DeviceSession?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_sessions.TryGetValue(id, out var s) ? s : null);

    public Task<DeviceSession?> GetByAnyTokenHashAsync(string refreshTokenHash, CancellationToken ct) =>
        Task.FromResult(_sessions.Values.FirstOrDefault(s =>
            s.RefreshTokenHash == refreshTokenHash || s.IsRetired(refreshTokenHash)));

    public Task<IReadOnlyList<DeviceSession>> ListByUserAsync(string userId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DeviceSession>>(_sessions.Values
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.LastUsedAt)
            .Take(limit)
            .ToList());

    public Task<bool> TryRotateAsync(
        string sessionId, string expectedHash, string newHash,
        DateTime newExpiresAt, string? ipAddress, DateTime now, CancellationToken ct)
    {
        if (FailRotate)
        {
            return Task.FromResult(false);
        }

        // In-memory compare-and-swap mirroring the store's atomic filter.
        if (!_sessions.TryGetValue(sessionId, out var s) ||
            s.RefreshTokenHash != expectedHash ||
            s.IsRevoked)
        {
            return Task.FromResult(false);
        }

        s.Rotate(newHash, newExpiresAt, ipAddress, now);
        return Task.FromResult(true);
    }

    public Task AddAsync(DeviceSession session, CancellationToken ct)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(DeviceSession session, CancellationToken ct)
    {
        _sessions[session.Id] = session;
        return Task.CompletedTask;
    }

    public Task RevokeAllForUserAsync(string userId, DateTime now, CancellationToken ct)
    {
        foreach (var s in _sessions.Values.Where(s => s.UserId == userId))
        {
            s.Revoke(now);
        }

        return Task.CompletedTask;
    }

    public Task RevokeAllForUserExceptAsync(string userId, string exceptSessionId, DateTime now, CancellationToken ct)
    {
        foreach (var s in _sessions.Values.Where(s => s.UserId == userId && s.Id != exceptSessionId))
        {
            s.Revoke(now);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken ct)
    {
        _sessions.Remove(id);
        return Task.CompletedTask;
    }
}

internal sealed class FakeAvatarStorage : IAvatarStorage
{
    private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _files = [];

    public Task<AvatarReference> SaveAsync(string userId, string contentType, byte[] bytes, CancellationToken ct)
    {
        var key = $"avatars/{userId}/fake";
        _files[key] = (bytes, contentType);
        return Task.FromResult(new AvatarReference(key, bytes.Length));
    }

    public Task<(byte[] Bytes, string ContentType)?> LoadAsync(string objectKey, CancellationToken ct) =>
        Task.FromResult(_files.TryGetValue(objectKey, out var f)
            ? ((byte[], string)?)f
            : null);

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        _files.Remove(objectKey);
        return Task.CompletedTask;
    }
}
