using Messenger.Application.Abstractions;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
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
    }

    public async Task<AvatarReference> SaveAsync(string userId, string contentType, byte[] bytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        ArgumentNullException.ThrowIfNull(bytes);

        var extension = contentType switch
        {
            "image/jpeg" => "jpg",
            "image/png" => "png",
            "image/webp" => "webp",
            _ => throw new ArgumentException($"Unsupported content type: {contentType}.", nameof(contentType))
        };

        var objectKey = $"avatars/{userId}/{Guid.NewGuid():n}.{extension}";
        var doc = new AvatarDocument
        {
            UserId = userId,
            ObjectKey = objectKey,
            ContentType = contentType,
            Bytes = bytes,
            UpdatedAt = clock.UtcNow
        };

        await Collection.ReplaceOneAsync(
            d => d.UserId == userId,
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
