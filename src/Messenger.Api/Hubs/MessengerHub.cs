using System.Security.Claims;
using Messenger.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Hubs;

/// <summary>
/// Single real-time hub (ADR-05). Hub events are delivery-only: every mutation
/// persists via REST first, then the endpoint fans out here. Reconnects re-sync
/// via <c>GET messages/sync</c> — SignalR is never durable storage.
///
/// Event classes:
/// <list type="bullet">
/// <item>Persistent (also in MongoDB, replayable via REST): <c>message:new</c>,
/// <c>message:edited</c>, <c>message:deleted</c>, <c>message:reacted</c>,
/// <c>message:read</c>, <c>message:delivered</c> watermarks, <c>chat:updated</c>.</item>
/// <item>Transient (never stored, expire client-side): <c>typing:update</c>,
/// <c>presence:update</c>, and call signaling <c>call:incoming</c>,
/// <c>call:accepted</c>, <c>call:rejected</c>, <c>call:joined</c>,
/// <c>call:left</c>, <c>call:ended</c> (call history re-syncs via REST).</item>
/// </list>
/// Sender identity always comes from the validated JWT — never from arguments.
/// </summary>
[Authorize]
public sealed class MessengerHub(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    IPresenceTracker presence,
    ILogger<MessengerHub> logger) : Hub
{
    public static string ChatGroup(string chatId) => $"chat:{chatId}";

    /// <summary>
    /// Per-user channel for membership changes (new chats, added/removed members).
    /// A recipient's open connections predate the chat group, so chat-group
    /// fan-out alone can never reach them — creation events go here instead.
    /// Joined server-side from the JWT subject only; no hub method lets a
    /// caller join anyone else's user group.
    /// </summary>
    public static string UserGroup(string userId) => $"user:{userId}";

    /// <summary>Max chats joined/broadcast per connection lifecycle pass.</summary>
    public const int MaxChatsPerPass = 200;

    private string? CallerId =>
        Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? Context.User?.FindFirst("sub")?.Value;

    public override async Task OnConnectedAsync()
    {
        logger.LogInformation("SignalR connected: {ConnectionId}", Context.ConnectionId);
        var callerId = CallerId;
        if (callerId is not null)
        {
            // Personal channel for membership changes (see UserGroup): the web
            // client refetches its chat list on chat:updated and subscribes to
            // new chat groups, so a chat created after this connection opened
            // still appears live instead of only after a refresh.
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(callerId)).ConfigureAwait(false);

            // Auto-join the caller's conversations so delivery works even if the
            // client's explicit Subscribe races with incoming messages.
            var chatIds = await ChatIdsForAsync(callerId).ConfigureAwait(false);
            foreach (var chatId in chatIds)
            {
                await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroup(chatId)).ConfigureAwait(false);
            }

            // 0→1 only: concurrent devices share one online state (no duplicate events).
            if (presence.Connected(callerId))
            {
                await BroadcastPresenceAsync(callerId, isOnline: true, lastSeenAt: null, chatIds)
                    .ConfigureAwait(false);
            }
        }

        await base.OnConnectedAsync().ConfigureAwait(false);
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("SignalR disconnected: {ConnectionId}", Context.ConnectionId);
        var callerId = CallerId;
        if (callerId is not null && presence.Disconnected(callerId))
        {
            // NOTE: Context.ConnectionAborted is already signaled here, so the
            // durable last-seen write + offline fan-out use a detached token.
            try
            {
                // 1→0 only: last-seen is durable, presence itself stays transient.
                var now = clock.UtcNow;
                var user = await users.GetByIdAsync(callerId, CancellationToken.None).ConfigureAwait(false);
                if (user is not null)
                {
                    user.TouchSeen(now);
                    await users.UpdateAsync(user, CancellationToken.None).ConfigureAwait(false);
                }

                var chatIds = await ChatIdsForAsync(callerId, CancellationToken.None).ConfigureAwait(false);
                await BroadcastPresenceAsync(callerId, isOnline: false, lastSeenAt: now, chatIds, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Presence offline handling failed for {ConnectionId}.", Context.ConnectionId);
            }
        }

        await base.OnDisconnectedAsync(exception).ConfigureAwait(false);
    }

    /// <summary>Joins caller to chat groups. Membership is verified per chat; outsiders are skipped.</summary>
    public async Task Subscribe(IEnumerable<string> chatIds)
    {
        ArgumentNullException.ThrowIfNull(chatIds);
        var callerId = CallerId;
        if (callerId is null)
        {
            return;
        }

        foreach (var chatId in chatIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(MaxChatsPerPass))
        {
            // Existence probe, not a full chat load: group joins only need the verdict.
            if (!await chats.IsMemberAsync(chatId, callerId, Context.ConnectionAborted).ConfigureAwait(false))
            {
                logger.LogWarning("Subscribe denied for {ConnectionId} to chat {ChatId}.",
                    Context.ConnectionId, chatId);
                continue;
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, ChatGroup(chatId)).ConfigureAwait(false);
        }
    }

    public async Task Unsubscribe(IEnumerable<string> chatIds)
    {
        ArgumentNullException.ThrowIfNull(chatIds);
        foreach (var chatId in chatIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().Take(MaxChatsPerPass))
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChatGroup(chatId)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Ephemeral typing signal. No DB write; server rebroadcasts to the chat
    /// group except the sender. Clients expire it locally (~5 s).
    /// </summary>
    public async Task Typing(string chatId, bool isTyping)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        var callerId = CallerId;
        if (callerId is null)
        {
            return;
        }

        // Hot path (every keystroke burst): membership probe without loading
        // the member array. Group membership itself is server-managed, so this
        // check is the whole authorization — no weaker than before.
        if (!await chats.IsMemberAsync(chatId, callerId, Context.ConnectionAborted).ConfigureAwait(false))
        {
            return;
        }

        await Clients.OthersInGroup(ChatGroup(chatId))
            .SendAsync("typing:update", new { chatId, userId = callerId, isTyping },
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Delivery acknowledgement for the caller's worst watermark. Persisted as a
    /// monotonic per-member watermark (stale/duplicate acks are ignored by the
    /// store), then fanned out as <c>message:delivered</c>. Sender identity is
    /// the JWT subject — the argument carries no user id.
    /// </summary>
    public async Task Delivered(string chatId, long seq)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        if (seq <= 0)
        {
            return;
        }

        var callerId = CallerId;
        if (callerId is null)
        {
            return;
        }

        // No separate membership read: MarkDeliveredAsync is a conditional
        // write whose filter already requires membership + live chat, so a
        // non-member ack simply advances nothing and fans out nothing.
        var advanced = await chats.MarkDeliveredAsync(
            chatId, callerId, seq, clock.UtcNow, Context.ConnectionAborted).ConfigureAwait(false);
        if (!advanced)
        {
            return;
        }

        await Clients.Group(ChatGroup(chatId))
            .SendAsync("message:delivered", new { chatId, userId = callerId, seq },
                Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ChatIdsForAsync(string userId)
    {
        // Id-only projection: connection setup needs group names, not member arrays.
        return await chats.ListIdsForUserAsync(userId, MaxChatsPerPass, Context.ConnectionAborted)
            .ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<string>> ChatIdsForAsync(string userId, CancellationToken ct)
    {
        return await chats.ListIdsForUserAsync(userId, MaxChatsPerPass, ct).ConfigureAwait(false);
    }

    private async Task BroadcastPresenceAsync(
        string userId, bool isOnline, DateTime? lastSeenAt, IReadOnlyList<string> chatIds)
    {
        var payload = new { userId, isOnline, lastSeenAt };
        foreach (var chatId in chatIds)
        {
            await Clients.Group(ChatGroup(chatId))
                .SendAsync("presence:update", payload, Context.ConnectionAborted)
                .ConfigureAwait(false);
        }
    }

    private async Task BroadcastPresenceAsync(
        string userId, bool isOnline, DateTime? lastSeenAt, IReadOnlyList<string> chatIds, CancellationToken ct)
    {
        var payload = new { userId, isOnline, lastSeenAt };
        foreach (var chatId in chatIds)
        {
            await Clients.Group(ChatGroup(chatId))
                .SendAsync("presence:update", payload, ct)
                .ConfigureAwait(false);
        }
    }
}
