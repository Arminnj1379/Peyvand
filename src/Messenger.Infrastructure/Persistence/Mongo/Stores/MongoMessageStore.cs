using Messenger.Application.Abstractions;
using Messenger.Domain.Messages;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
using Messenger.Infrastructure.Persistence.Mongo.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>MongoDB <see cref="IMessageStore"/> (collection <c>messages</c>).</summary>
public sealed class MongoMessageStore(MongoDbContext context) : IMessageStore
{
    public const string CollectionName = "messages";

    private IMongoCollection<MessageDocument> Collection =>
        context.Collection<MessageDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        // Primary history access path: per-chat seq pages.
        await keys.CreateOneAsync(
            new CreateIndexModel<MessageDocument>(
                Builders<MessageDocument>.IndexKeys.Ascending(d => d.ChatId).Descending(d => d.Seq),
                new CreateIndexOptions { Name = "ix_history" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Idempotency: one clientMessageId per sender (retries can never duplicate).
        // Sparse so sender-less system messages never collide on null.
        await keys.CreateOneAsync(
            new CreateIndexModel<MessageDocument>(
                Builders<MessageDocument>.IndexKeys.Ascending(d => d.SenderId).Ascending(d => d.ClientMessageId),
                new CreateIndexOptions { Unique = true, Sparse = true, Name = "ux_sender_client" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Cursor fallback + sync scans.
        await keys.CreateOneAsync(
            new CreateIndexModel<MessageDocument>(
                Builders<MessageDocument>.IndexKeys.Ascending(d => d.ChatId).Descending(d => d.Id),
                new CreateIndexOptions { Name = "ix_chat_id" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<MessageDocument>(
                Builders<MessageDocument>.IndexKeys.Text(d => d.Body),
                new CreateIndexOptions { Name = "txt_body" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Reply threads: direct children of a message, oldest-first.
        await keys.CreateOneAsync(
            new CreateIndexModel<MessageDocument>(
                Builders<MessageDocument>.IndexKeys.Ascending(d => d.ChatId)
                    .Ascending(d => d.ReplyToMessageId).Ascending(d => d.Seq),
                new CreateIndexOptions { Name = "ix_thread" }),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<Message?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : MessageMapper.ToDomain(doc);
    }

    public async Task<Message?> GetByClientKeyAsync(string senderId, string clientMessageId, CancellationToken ct)
    {
        var doc = await Collection
            .Find(d => d.SenderId == senderId && d.ClientMessageId == clientMessageId)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : MessageMapper.ToDomain(doc);
    }

    public async Task<Message?> GetByChatAndSeqAsync(string chatId, long seq, CancellationToken ct)
    {
        var doc = await Collection
            .Find(d => d.ChatId == chatId && d.Seq == seq)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : MessageMapper.ToDomain(doc);
    }

    public async Task AddAsync(Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        try
        {
            await Collection.InsertOneAsync(MessageMapper.ToDocument(message), cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            throw new DuplicateMessageException();
        }
    }

    public async Task UpdateAsync(Message message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var result = await Collection
            .ReplaceOneAsync(d => d.Id == message.Id, MessageMapper.ToDocument(message), cancellationToken: ct)
            .ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            throw new InvalidOperationException($"Message {message.Id} does not exist.");
        }
    }

    public async Task<IReadOnlyList<Message>> ListHistoryAsync(
        string chatId, long? beforeSeq, int limitPlusOne, CancellationToken ct)
    {
        var builder = Builders<MessageDocument>.Filter;
        var filter = beforeSeq.HasValue
            ? builder.And(builder.Eq(d => d.ChatId, chatId), builder.Lt(d => d.Seq, beforeSeq.Value))
            : builder.Eq(d => d.ChatId, chatId);
        var docs = await Collection.Find(filter)
            .Sort(Builders<MessageDocument>.Sort.Descending(d => d.Seq))
            .Limit(limitPlusOne)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(MessageMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Message>> ListAfterAsync(string chatId, long afterSeq, int limit, CancellationToken ct)
    {
        var builder = Builders<MessageDocument>.Filter;
        var docs = await Collection.Find(
                builder.And(builder.Eq(d => d.ChatId, chatId), builder.Gt(d => d.Seq, afterSeq)))
            .Sort(Builders<MessageDocument>.Sort.Ascending(d => d.Seq))
            .Limit(limit)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(MessageMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Message>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var valid = ids.Where(id => ObjectId.TryParse(id, out _)).ToList();
        if (valid.Count == 0)
        {
            return [];
        }

        var docs = await Collection.Find(d => valid.Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(MessageMapper.ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Message>> ListRepliesAsync(
        string chatId, string parentMessageId, int limit, CancellationToken ct)
    {
        if (!ObjectId.TryParse(chatId, out _) || !ObjectId.TryParse(parentMessageId, out _))
        {
            return [];
        }

        // Chat-scoped so ix_thread (chatId, replyToMessageId, seq) serves the
        // filter and the sort — no collection scan, no in-memory sort.
        var docs = await Collection.Find(
                d => d.ChatId == chatId && d.ReplyToMessageId == parentMessageId)
            .Sort(Builders<MessageDocument>.Sort.Ascending(d => d.Seq))
            .Limit(limit)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(MessageMapper.ToDomain).ToList();
    }

    public async Task IncrementReplyCountAsync(string messageId, CancellationToken ct)
    {
        if (!ObjectId.TryParse(messageId, out _))
        {
            return;
        }

        await Collection.UpdateOneAsync(
            d => d.Id == messageId,
            Builders<MessageDocument>.Update.Inc(d => d.ReplyCount, 1),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Message>> SearchAsync(
        IReadOnlyList<string> chatIds, string query, int limit, CancellationToken ct)
    {
        var valid = chatIds.Where(id => ObjectId.TryParse(id, out _)).ToList();
        if (valid.Count == 0 || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var builder = Builders<MessageDocument>.Filter;
        var docs = await Collection.Find(
                builder.And(
                    builder.In(d => d.ChatId, valid),
                    builder.Text(query)))
            .Sort(Builders<MessageDocument>.Sort.Descending(d => d.CreatedAt))
            .Limit(limit)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(MessageMapper.ToDomain).ToList();
    }
}
