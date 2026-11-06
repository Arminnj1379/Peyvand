using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo;

/// <summary>
/// Index bootstrap helper. <c>CreateOneAsync</c> is idempotent only when name,
/// keys, AND options all match; on an options conflict (Mongo error 85) the
/// old definition is dropped and recreated so a corrected definition rolls
/// out with the deploy instead of crashing startup.
/// <para/>
/// Use ONLY for indexes introduced alongside this helper (zero production
/// history). Never auto-recreate pre-existing indexes: dropping a live
/// unique index, even briefly, risks duplicate writes slipping through.
/// </summary>
internal static class MongoIndex
{
    public static async Task CreateOneHandlingConflictAsync<TDocument>(
        IMongoCollection<TDocument> collection,
        CreateIndexModel<TDocument> model,
        ILogger logger,
        CancellationToken ct)
    {
        try
        {
            await collection.Indexes.CreateOneAsync(model, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoCommandException ex) when (ex.Code is 85 or 86)
        {
            // 85 IndexOptionsConflict / 86 IndexKeySpecsConflict: same name,
            // different definition. The name is owned by this bootstrap (new
            // indexes only), so replacing the stale definition is safe.
            logger.LogWarning(ex, "Index {IndexName} definition changed; dropping and recreating.",
                model.Options?.Name);
            await collection.Indexes.DropOneAsync(model.Options?.Name, ct).ConfigureAwait(false);
            await collection.Indexes.CreateOneAsync(model, cancellationToken: ct).ConfigureAwait(false);
        }
    }
}
