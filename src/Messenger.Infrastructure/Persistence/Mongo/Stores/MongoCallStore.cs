using Messenger.Application.Abstractions;
using Messenger.Domain.Calls;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
using Messenger.Infrastructure.Persistence.Mongo.Mapping;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>MongoDB <see cref="ICallStore"/> (collection <c>calls</c>). Signaling state only.</summary>
public sealed class MongoCallStore(MongoDbContext context, ILogger<MongoCallStore> logger) : ICallStore
{
    public const string CollectionName = "calls";

    private IMongoCollection<CallDocument> Collection =>
        context.Collection<CallDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        // Per-chat history (newest first) + live-call lookup.
        await keys.CreateOneAsync(
            new CreateIndexModel<CallDocument>(
                Builders<CallDocument>.IndexKeys.Ascending(d => d.ChatId).Descending(d => d.CreatedAt),
                new CreateIndexOptions { Name = "ix_chat_created" }),
            cancellationToken: ct).ConfigureAwait(false);
        // One live call per chat: concurrent starters collide on insert (409 →
        // return active) instead of creating parallel calls. Ended calls are
        // excluded so history stays unbounded.
        await MongoIndex.CreateOneHandlingConflictAsync(
            Collection,
            new CreateIndexModel<CallDocument>(
                Builders<CallDocument>.IndexKeys.Ascending(d => d.ChatId),
                new CreateIndexOptions<CallDocument>
                {
                    Unique = true,
                    Name = "ux_live_call",
                    PartialFilterExpression = Builders<CallDocument>.Filter.In(
                        d => d.Status, [CallStatus.Ringing, CallStatus.Active])
                }),
            logger,
            ct).ConfigureAwait(false);
    }

    public async Task<Call?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : CallMapper.ToDomain(doc);
    }

    public async Task<Call?> GetActiveForChatAsync(string chatId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(chatId, out _))
        {
            return null;
        }

        var doc = await Collection
            .Find(d => d.ChatId == chatId &&
                (d.Status == CallStatus.Ringing || d.Status == CallStatus.Active))
            .SortByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : CallMapper.ToDomain(doc);
    }

    public async Task<IReadOnlyList<Call>> ListForChatAsync(string chatId, int limit, CancellationToken ct)
    {
        if (!ObjectId.TryParse(chatId, out _))
        {
            return [];
        }

        var docs = await Collection
            .Find(d => d.ChatId == chatId)
            .SortByDescending(d => d.CreatedAt)
            .Limit(limit)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(CallMapper.ToDomain).ToList();
    }

    public async Task AddAsync(Call call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        try
        {
            await Collection.InsertOneAsync(CallMapper.ToDocument(call), cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateCallException();
        }
    }

    public async Task UpdateAsync(Call call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call);
        var result = await Collection
            .ReplaceOneAsync(d => d.Id == call.Id, CallMapper.ToDocument(call), cancellationToken: ct)
            .ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            throw new InvalidOperationException($"Call {call.Id} does not exist.");
        }
    }
}
