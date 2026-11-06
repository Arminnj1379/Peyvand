using Messenger.Application.Abstractions;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>
/// Mongo-backed <see cref="IAvatarStorage"/> (collection <c>avatars</c>).
/// Holds small avatar images until the Media/MinIO slice provides general
/// file storage; the port keeps that migration internal to Infrastructure.
/// </summary>
public sealed class MongoAvatarStorage(MongoDbContext context, IClock clock) : IAvatarStorage
{
    public const string CollectionName = "avatars";

    private IMongoCollection<AvatarDocument> Collection =>
        context.Collection<AvatarDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        // Avatar lookups are by objectKey (stored on user/chat docs), not _id:
        // without this every avatar view is a collection scan. Keys are always
        // set and unique per upload, so unique (non-sparse) is safe.
        await keys.CreateOneAsync(
            new CreateIndexModel<AvatarDocument>(
                Builders<AvatarDocument>.IndexKeys.Ascending(d => d.ObjectKey),
                new CreateIndexOptions { Unique = true, Name = "ux_object_key" }),
            cancellationToken: ct).ConfigureAwait(false);
        // One avatar per owner (user id, or chat-{chatId} for groups/channels).
        // Sparse: rows written before ownerKey existed have no such element.
        await keys.CreateOneAsync(
            new CreateIndexModel<AvatarDocument>(
                Builders<AvatarDocument>.IndexKeys.Ascending(d => d.OwnerKey),
                new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_owner_key" }),
            cancellationToken: ct).ConfigureAwait(false);
    }

    /// <param name="ownerKey">Owner namespace: user id, or chat-{chatId} for group/channel avatars.</param>
    public async Task<AvatarReference> SaveAsync(string ownerKey, string contentType, byte[] bytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(bytes);

        var extension = contentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => throw new ArgumentException($"Unsupported content type: {contentType}.", nameof(contentType))
        };

        var objectKey = $"avatars/{ownerKey}/{Guid.NewGuid():n}.{extension}";

        // Rows written before ownerKey existed keyed the owner id as _id (valid
        // only for user ids, never for chat-{id}). Drop the stale row so each
        // owner keeps exactly one avatar; reuses no _id so ReplaceOne below
        // never trips the immutable-_id rule.
        var existing = await Collection.Find(d => d.OwnerKey == ownerKey).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (existing is null && ObjectId.TryParse(ownerKey, out _))
        {
            await Collection.DeleteOneAsync(d => d.UserId == ownerKey, ct).ConfigureAwait(false);
        }

        var doc = new AvatarDocument
        {
            UserId = existing?.UserId ?? ObjectId.GenerateNewId().ToString(),
            OwnerKey = ownerKey,
            ObjectKey = objectKey,
            ContentType = contentType,
            Bytes = bytes,
            UpdatedAt = clock.UtcNow
        };

        await Collection.ReplaceOneAsync(
            d => d.OwnerKey == ownerKey,
            doc,
            new ReplaceOptions { IsUpsert = true },
            ct).ConfigureAwait(false);

        return new AvatarReference(objectKey, bytes.Length);
    }

    public async Task<(byte[] Bytes, string ContentType)?> LoadAsync(string objectKey, CancellationToken ct)
    {
        var doc = await Collection.Find(d => d.ObjectKey == objectKey).FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        return doc is null ? null : (doc.Bytes, doc.ContentType);
    }

    public async Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        await Collection.DeleteOneAsync(d => d.ObjectKey == objectKey, ct).ConfigureAwait(false);
    }
}
