namespace Messenger.Api.Hubs;

/// <summary>
/// Online/offline presence. Connection counts are held in memory, so this is
/// exact for a single API instance. When scaling out (Redis backplane), route
/// SignalR connections with stickiness or treat presence as best-effort:
/// broadcasts still fan out, but counts are per-instance.
/// Last-seen timestamps are durable (persisted on `users`), presence itself is
/// transient and never stored.
/// </summary>
public interface IPresenceTracker
{
    /// <summary>Registers a connection. Returns true on the 0→1 transition.</summary>
    bool Connected(string userId);

    /// <summary>Removes a connection. Returns true on the 1→0 transition.</summary>
    bool Disconnected(string userId);

    bool IsOnline(string userId);
}

public sealed class InMemoryPresenceTracker : IPresenceTracker
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _connections = new(StringComparer.Ordinal);

    public bool Connected(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        lock (_gate)
        {
            _connections.TryGetValue(userId, out var count);
            _connections[userId] = count + 1;
            return count == 0;
        }
    }

    public bool Disconnected(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        lock (_gate)
        {
            if (!_connections.TryGetValue(userId, out var count) || count <= 1)
            {
                _connections.Remove(userId);
                return true;
            }

            _connections[userId] = count - 1;
            return false;
        }
    }

    public bool IsOnline(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        lock (_gate)
        {
            return _connections.ContainsKey(userId);
        }
    }
}
