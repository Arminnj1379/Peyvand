using Messenger.Application.Abstractions;
using Messenger.Domain.Calls;

namespace Messenger.Application.Tests;

/// <summary>In-memory call port for handler tests. No Mongo, no LiveKit server.</summary>
internal sealed class FakeCallStore : ICallStore
{
    private readonly Dictionary<string, Call> _calls = [];

    /// <summary>Simulates losing the live-call insert race (unique index collision).</summary>
    public bool ThrowOnAdd { get; set; }

    public Task<Call?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_calls.TryGetValue(id, out var c) ? c : null);

    public Task<Call?> GetActiveForChatAsync(string chatId, CancellationToken ct) =>
        Task.FromResult(_calls.Values
            .Where(c => c.ChatId == chatId && c.IsLive)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault());

    public Task<IReadOnlyList<Call>> ListForChatAsync(string chatId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<Call>>(_calls.Values
            .Where(c => c.ChatId == chatId)
            .OrderByDescending(c => c.CreatedAt)
            .Take(limit)
            .ToList());

    public Task AddAsync(Call call, CancellationToken ct)
    {
        if (ThrowOnAdd)
        {
            throw new DuplicateCallException();
        }

        // Mirrors the ux_live_call partial unique index: a second live call
        // for the same chat collides instead of stacking.
        if (_calls.Values.Any(c => c.Id != call.Id && c.ChatId == call.ChatId && c.IsLive) && call.IsLive)
        {
            throw new DuplicateCallException();
        }

        _calls[call.Id] = call;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(Call call, CancellationToken ct)
    {
        _calls[call.Id] = call;
        return Task.CompletedTask;
    }
}

internal sealed class FakeLiveKit : ILiveKitTokenService
{
    public string ServerUrl => "wss://livekit.test";
    public bool IsConfigured { get; set; } = true;
    public List<(string Room, string Identity)> Issued { get; } = [];

    public string CreateJoinToken(string roomName, string identity, string displayName)
    {
        Issued.Add((roomName, identity));
        return $"token-{roomName}-{identity}";
    }
}
