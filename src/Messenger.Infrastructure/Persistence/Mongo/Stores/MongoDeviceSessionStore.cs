using Messenger.Application.Abstractions;
using Messenger.Domain.Sessions;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
using Messenger.Infrastructure.Persistence.Mongo.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>MongoDB <see cref="IDeviceSessionStore"/> (collection <c>device_sessions</c>).</summary>
public sealed class MongoDeviceSessionStore(MongoDbContext context) : IDeviceSessionStore
{
    public const string CollectionName = "device_sessions";

    private IMongoCollection<DeviceSessionDocument> Collection =>
        context.Collection<DeviceSessionDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        await keys.CreateOneAsync(
            new CreateIndexModel<DeviceSessionDocument>(
                Builders<DeviceSessionDocument>.IndexKeys.Ascending(d => d.RefreshTokenHash),
                new CreateIndexOptions { Unique = true, Name = "ux_refresh_hash" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<DeviceSessionDocument>(
                Builders<DeviceSessionDocument>.IndexKeys.Ascending(d => d.RetiredTokenHashes),
                new CreateIndexOptions { Name = "ix_retired_hashes" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<DeviceSessionDocument>(
                Builders<DeviceSessionDocument>.IndexKeys.Ascending(d => d.UserId),
                new CreateIndexOptions { Name = "ix_user" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Expired sessions delete themselves; revoked ones are kept for audit.
        await keys.CreateOneAsync(
            new CreateIndexModel<DeviceSessionDocument>(
                Builders<DeviceSessionDocument>.IndexKeys.Ascending(d => d.ExpiresAt),
                new CreateIndexOptions { ExpireAfter = TimeSpan.Zero, Name = "ttl_expires" }),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<DeviceSession?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : SessionMapper.ToDomain(doc);
    }

    public async Task<DeviceSession?> GetByAnyTokenHashAsync(string refreshTokenHash, CancellationToken ct)
    {
        var filter = Builders<DeviceSessionDocument>.Filter.Or(
            Builders<DeviceSessionDocument>.Filter.Eq(d => d.RefreshTokenHash, refreshTokenHash),
            Builders<DeviceSessionDocument>.Filter.AnyEq(d => d.RetiredTokenHashes, refreshTokenHash));
        var doc = await Collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : SessionMapper.ToDomain(doc);
    }

    public async Task<IReadOnlyList<DeviceSession>> ListByUserAsync(
        string userId, int limit, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return [];
        }

        // Bounded + server-sorted: sessions grow per login and only TTL trims
        // them, so an unbounded list is a load amplifier on this endpoint.
        var docs = await Collection.Find(d => d.UserId == userId)
            .Sort(Builders<DeviceSessionDocument>.Sort.Descending(d => d.LastUsedAt))
            .Limit(limit)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(SessionMapper.ToDomain).ToList();
    }

    public async Task<bool> TryRotateAsync(
        string sessionId, string expectedHash, string newHash,
        DateTime newExpiresAt, string? ipAddress, DateTime now, CancellationToken ct)
    {
        if (!ObjectId.TryParse(sessionId, out _) ||
            string.IsNullOrWhiteSpace(expectedHash) ||
            string.IsNullOrWhiteSpace(newHash))
        {
            return false;
        }

        // Atomic compare-and-swap: the rotation lands only when the presented
        // hash is still current. Concurrent refreshes serialize here instead of
        // clobbering each other's retired-hash list via full-doc replace.
        // $push/$slice keeps the retired list bounded exactly like the domain.
        var filter = Builders<DeviceSessionDocument>.Filter.And(
            Builders<DeviceSessionDocument>.Filter.Eq(d => d.Id, sessionId),
            Builders<DeviceSessionDocument>.Filter.Eq(d => d.RefreshTokenHash, expectedHash),
            Builders<DeviceSessionDocument>.Filter.Eq(d => d.RevokedAt, null));
        var update = Builders<DeviceSessionDocument>.Update
            .Set(d => d.RefreshTokenHash, newHash)
            .Set(d => d.ExpiresAt, newExpiresAt)
            .Set(d => d.LastUsedAt, now)
            .Set(d => d.IpAddress, ipAddress)
            .PushEach(d => d.RetiredTokenHashes, [expectedHash], slice: -DeviceSession.MaxRetiredHashes);
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: ct)
            .ConfigureAwait(false);
        return result.ModifiedCount == 1;
    }

    public async Task AddAsync(DeviceSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        await Collection.InsertOneAsync(SessionMapper.ToDocument(session), cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(DeviceSession session, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(session);
        var result = await Collection
            .ReplaceOneAsync(d => d.Id == session.Id, SessionMapper.ToDocument(session), cancellationToken: ct)
            .ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            throw new InvalidOperationException($"Session {session.Id} does not exist.");
        }
    }

    public async Task RevokeAllForUserAsync(string userId, DateTime now, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return;
        }

        var update = Builders<DeviceSessionDocument>.Update
            .Set(d => d.RevokedAt, now);
        await Collection
            .UpdateManyAsync(
                d => d.UserId == userId && d.RevokedAt == null,
                update,
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task RevokeAllForUserExceptAsync(string userId, string exceptSessionId, DateTime now, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userId, out _) || !ObjectId.TryParse(exceptSessionId, out _))
        {
            return;
        }

        var update = Builders<DeviceSessionDocument>.Update
            .Set(d => d.RevokedAt, now);
        await Collection
            .UpdateManyAsync(
                d => d.UserId == userId && d.Id != exceptSessionId && d.RevokedAt == null,
                update,
                cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return;
        }

        await Collection.DeleteOneAsync(d => d.Id == id, ct).ConfigureAwait(false);
    }
}
