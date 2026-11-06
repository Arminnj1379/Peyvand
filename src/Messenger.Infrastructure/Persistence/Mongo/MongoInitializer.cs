using Messenger.Application.Abstractions;
using Messenger.Infrastructure.Persistence.Mongo.Stores;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Messenger.Infrastructure.Persistence.Mongo;

/// <summary>Creates auth + messaging indexes at startup (idempotent).</summary>
public sealed class MongoInitializer(
    MongoUserStore users,
    MongoDeviceSessionStore sessions,
    MongoAvatarStorage avatars,
    MongoChatStore chats,
    MongoMessageStore messages,
    MongoFileStore files,
    MongoCallStore calls,
    MongoTicketStore tickets,
    IFileStorage storage,
    ILogger<MongoInitializer> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await users.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        await sessions.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        await avatars.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        await chats.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        await messages.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        await files.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        await calls.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        await tickets.EnsureIndexesAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("MongoDB auth + messaging + files + calls + tickets indexes ensured.");

        // Best-effort: text chat must boot even when object storage is down;
        // uploads surface file.storage_unavailable (503) instead.
        try
        {
            await storage.EnsureBucketAsync(cancellationToken).ConfigureAwait(false);
            logger.LogInformation("Object storage bucket ensured.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Object storage unreachable at startup; file uploads will report unavailable.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
