using Messenger.Application.Abstractions;
using Messenger.Domain.Chats;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
using Messenger.Infrastructure.Persistence.Mongo.Mapping;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>MongoDB <see cref="IChatStore"/> (collection <c>chats</c>). Driver calls directly.</summary>
public sealed class MongoChatStore(MongoDbContext context, ILogger<MongoChatStore> logger) : IChatStore
{
    public const string CollectionName = "chats";

    private IMongoCollection<ChatDocument> Collection => context.Collection<ChatDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        await keys.CreateOneAsync(
            new CreateIndexModel<ChatDocument>(
                Builders<ChatDocument>.IndexKeys.Ascending("members.userId").Descending(d => d.LastMessageAt),
                new CreateIndexOptions { Name = "ix_member_list" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<ChatDocument>(
                Builders<ChatDocument>.IndexKeys.Ascending(d => d.Type).Descending(d => d.LastMessageAt),
                new CreateIndexOptions { Name = "ix_type_list" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Public channel handles: unique handle lookup for subscribe-by-username.
        await keys.CreateOneAsync(
            new CreateIndexModel<ChatDocument>(
                Builders<ChatDocument>.IndexKeys.Ascending(d => d.Username),
                new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_channel_username" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Revocable invite links: unique opaque code lookup for join-by-link.
        await keys.CreateOneAsync(
            new CreateIndexModel<ChatDocument>(
                Builders<ChatDocument>.IndexKeys.Ascending(d => d.InviteCode),
                new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_invite_code" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Deterministic DM pair key: makes concurrent creates for the same pair
        // collide on insert (409 → return existing) instead of duplicating.
        // Partial (live docs only) so a re-created DM after a soft delete does
        // not collide with its deleted predecessor. Group/channel chats and
        // legacy DMs carry no key and are simply not indexed.
        // NOTE: the partial filter must also require the field itself ($exists):
        // a unique index treats "field missing" as one shared null entry, so
        // without $exists the second keyless chat would collide.
        await MongoIndex.CreateOneHandlingConflictAsync(
            Collection,
            new CreateIndexModel<ChatDocument>(
                Builders<ChatDocument>.IndexKeys.Ascending(d => d.DmKey),
                new CreateIndexOptions<ChatDocument>
                {
                    Unique = true,
                    Name = "ux_dm_key",
                    PartialFilterExpression = Builders<ChatDocument>.Filter.And(
                        Builders<ChatDocument>.Filter.Eq(d => d.IsDeleted, false),
                        Builders<ChatDocument>.Filter.Exists(d => d.DmKey))
                }),
            logger,
            ct).ConfigureAwait(false);
    }

    public async Task<Chat?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : ChatMapper.ToDomain(doc);
    }

    public async Task<Chat?> FindDmAsync(string userA, string userB, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userA, out _) || !ObjectId.TryParse(userB, out _))
        {
            return null;
        }

        // Fast path: deterministic pair key (indexed). Live docs only, matching
        // the legacy path below; legacy DMs written before dmKey fall through.
        var key = Chat.BuildDmKey(userA, userB);
        var hit = await Collection.Find(d => d.DmKey == key && d.IsDeleted == false)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (hit is not null)
        {
            return ChatMapper.ToDomain(hit);
        }

        // Legacy path: DM with exactly these two members (raw filter on the embedded array).
        var filter = new BsonDocumentFilterDefinition<ChatDocument>(new BsonDocument
        {
            { "type", ChatType.Dm.ToString() },
            { "memberCount", 2 },
            { "isDeleted", false },
            { "members.userId", new BsonDocument("$all", new BsonArray { new ObjectId(userA), new ObjectId(userB) }) }
        });
        var doc = await Collection.Find(filter).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : ChatMapper.ToDomain(doc);
    }

    public async Task<Chat?> GetByInviteCodeAsync(string inviteCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(inviteCode))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.InviteCode == inviteCode.Trim())
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : ChatMapper.ToDomain(doc);
    }

    public async Task<Chat?> GetByUsernameAsync(string username, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            return null;
        }

        var normalized = username.Trim().ToLowerInvariant();
        var doc = await Collection.Find(d => d.Username == normalized)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : ChatMapper.ToDomain(doc);
    }

    public async Task<IReadOnlyList<Chat>> ListForUserAsync(string userId, int limit, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return [];
        }

        var filter = Builders<ChatDocument>.Filter.And(
            Builders<ChatDocument>.Filter.Eq("members.userId", new ObjectId(userId)),
            Builders<ChatDocument>.Filter.Eq(d => d.IsDeleted, false));
        var docs = await Collection.Find(filter)
            .Sort(Builders<ChatDocument>.Sort.Descending(d => d.LastMessageAt).Descending(d => d.UpdatedAt))
            .Limit(limit)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(ChatMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<string>> ListIdsForUserAsync(string userId, int limit, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userId, out _))
        {
            return [];
        }

        // Same filter/sort as ListForUserAsync, but only _id crosses the wire:
        // connection setup needs ids, not 500-member arrays.
        var filter = Builders<ChatDocument>.Filter.And(
            Builders<ChatDocument>.Filter.Eq("members.userId", new ObjectId(userId)),
            Builders<ChatDocument>.Filter.Eq(d => d.IsDeleted, false));
        return await Collection.Find(filter)
            .Sort(Builders<ChatDocument>.Sort.Descending(d => d.LastMessageAt).Descending(d => d.UpdatedAt))
            .Limit(limit)
            .Project(d => d.Id)
            .ToListAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> IsMemberAsync(string chatId, string userId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(chatId, out _) || !ObjectId.TryParse(userId, out _))
        {
            return false;
        }

        // Existence probe for hot paths (hub invokes): no document payload,
        // same verdict as loading the chat and calling IsMember.
        var filter = Builders<ChatDocument>.Filter.And(
            Builders<ChatDocument>.Filter.Eq(d => d.Id, chatId),
            Builders<ChatDocument>.Filter.Eq(d => d.IsDeleted, false),
            Builders<ChatDocument>.Filter.Eq("members.userId", new ObjectId(userId)));
        return await Collection.Find(filter).Project(d => d.Id).AnyAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> SharesChatAsync(string userA, string userB, CancellationToken ct)
    {
        if (!ObjectId.TryParse(userA, out _) || !ObjectId.TryParse(userB, out _))
        {
            return false;
        }

        // Any live conversation containing both users (DM or group/channel).
        var filter = Builders<ChatDocument>.Filter.And(
            Builders<ChatDocument>.Filter.Eq(d => d.IsDeleted, false),
            Builders<ChatDocument>.Filter.Eq("members.userId", new ObjectId(userA)),
            Builders<ChatDocument>.Filter.Eq("members.userId", new ObjectId(userB)));
        return await Collection.Find(filter).Project(d => d.Id).AnyAsync(ct).ConfigureAwait(false);
    }

    public async Task AddAsync(Chat chat, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chat);
        try
        {
            await Collection.InsertOneAsync(ChatMapper.ToDocument(chat), cancellationToken: ct).ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateChatException();
        }
    }

    public async Task UpdateAsync(Chat chat, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chat);
        try
        {
            var result = await Collection
                .ReplaceOneAsync(d => d.Id == chat.Id, ChatMapper.ToDocument(chat), cancellationToken: ct)
                .ConfigureAwait(false);
            if (result.MatchedCount == 0)
            {
                throw new InvalidOperationException($"Chat {chat.Id} does not exist.");
            }
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // Lost a concurrent claim on the unique channel username or invite code.
            throw new DuplicateChatException();
        }
    }

    public async Task<long> AllocateSeqAsync(string chatId, DateTime now, CancellationToken ct)
    {
        var update = Builders<ChatDocument>.Update
            .Inc(d => d.SeqCounter, 1)
            .Set(d => d.UpdatedAt, now);
        var options = new FindOneAndUpdateOptions<ChatDocument> { ReturnDocument = ReturnDocument.After };
        var doc = await Collection.FindOneAndUpdateAsync(
            d => d.Id == chatId, update, options, ct).ConfigureAwait(false);
        if (doc is null)
        {
            throw new InvalidOperationException($"Chat {chatId} does not exist.");
        }

        return doc.SeqCounter;
    }

    public async Task ApplyNewMessageAsync(
        string chatId, long seq, string messageId, string preview, string senderId, DateTime at,
        CancellationToken ct)
    {
        var update = Builders<ChatDocument>.Update
            .Set(d => d.LastMessageId, messageId)
            .Set(d => d.LastMessageAt, at)
            .Set(d => d.LastMessagePreview, preview)
            .Set(d => d.UpdatedAt, at)
            .Set(d => d.SeqCounter, seq)
            .Inc("members.$[other].unreadCount", 1)
            .Set("members.$[self].lastReadSeq", seq)
            .Set("members.$[self].lastReadAt", at)
            .Set("members.$[self].unreadCount", 0)
            .Set("members.$[self].lastDeliveredSeq", seq);

        var options = new UpdateOptions
        {
            ArrayFilters =
            [
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("other.userId", new BsonDocument("$ne", new ObjectId(senderId)))),
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("self.userId", new ObjectId(senderId)))
            ]
        };

        await Collection.UpdateOneAsync(d => d.Id == chatId, update, options, ct).ConfigureAwait(false);
    }

    public async Task<bool> MarkReadAsync(string chatId, string userId, long seq, DateTime at, CancellationToken ct)
    {
        if (!ObjectId.TryParse(chatId, out _) || !ObjectId.TryParse(userId, out _))
        {
            return false;
        }

        // Single conditional pipeline update (was: full-doc read + in-memory
        // check + second write). The ElemMatch advances the watermark only when
        // stale (monotonic — concurrent readers cannot regress it); unreadCount
        // derives from the live seqCounter server-side. $map is used because
        // pipeline updates support neither arrayFilters nor the positional $
        // operator; $convert keeps the int field type (BSON int64 would break
        // reads).
        var filter = Builders<ChatDocument>.Filter.And(
            Builders<ChatDocument>.Filter.Eq(d => d.Id, chatId),
            Builders<ChatDocument>.Filter.ElemMatch(
                d => d.Members,
                m => m.UserId == userId && m.LastReadSeq < seq));
        var update = Builders<ChatDocument>.Update.Pipeline(
            PipelineDefinition<ChatDocument, ChatDocument>.Create(
                new[]
                {
                    new BsonDocument("$set", new BsonDocument
                    {
                        { "members", new BsonDocument("$map", new BsonDocument
                            {
                                { "input", "$members" },
                                { "as", "m" },
                                { "in", new BsonDocument("$mergeObjects", new BsonArray
                                    {
                                        "$$m",
                                        new BsonDocument("$cond", new BsonDocument
                                        {
                                            { "if", new BsonDocument("$eq", new BsonArray { "$$m.userId", new ObjectId(userId) }) },
                                            { "then", new BsonDocument
                                                {
                                                    { "lastReadSeq", seq },
                                                    { "lastReadAt", at },
                                                    { "unreadCount", new BsonDocument("$max", new BsonArray
                                                        {
                                                            0,
                                                            new BsonDocument("$convert", new BsonDocument
                                                            {
                                                                { "input", new BsonDocument("$subtract", new BsonArray { "$seqCounter", seq }) },
                                                                { "to", "int" }
                                                            })
                                                        }) }
                                                } },
                                            { "else", new BsonDocument() }
                                        })
                                    }) }
                            }) },
                        { "updatedAt", at }
                    })
                }));
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: ct)
            .ConfigureAwait(false);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> MarkDeliveredAsync(string chatId, string userId, long seq, DateTime at, CancellationToken ct)
    {
        if (!ObjectId.TryParse(chatId, out _) || !ObjectId.TryParse(userId, out _) || seq <= 0)
        {
            return false;
        }

        // Monotonic compare-and-set: stale or duplicate acks never move the watermark.
        var filter = Builders<ChatDocument>.Filter.And(
            Builders<ChatDocument>.Filter.Eq(d => d.Id, chatId),
            Builders<ChatDocument>.Filter.Eq(d => d.IsDeleted, false),
            Builders<ChatDocument>.Filter.ElemMatch(
                d => d.Members,
                m => m.UserId == userId && m.LastDeliveredSeq < seq),
            Builders<ChatDocument>.Filter.Gte(d => d.SeqCounter, seq));
        var update = Builders<ChatDocument>.Update
            .Set("members.$.lastDeliveredSeq", seq)
            .Set(d => d.UpdatedAt, at);
        var result = await Collection.UpdateOneAsync(filter, update, cancellationToken: ct)
            .ConfigureAwait(false);
        return result.ModifiedCount > 0;
    }
}
