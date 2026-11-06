using Messenger.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Push;

/// <summary>
/// Air-gap-safe default: logs and completes. Replaced by the Firebase sender
/// when push is configured (Phase 2). In-app delivery always works regardless.
/// </summary>
public sealed class NoOpPushSender(ILogger<NoOpPushSender> logger) : IPushSender
{
    public Task SendAsync(string userId, string title, string body, CancellationToken cancellationToken)
    {
        // Never log message bodies — ids and lengths only (PII rule).
        logger.LogWarning("Push not configured; skipping notification for user {UserId}.", userId);
        return Task.CompletedTask;
    }
}
