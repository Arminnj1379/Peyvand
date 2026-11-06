using Messenger.Application.Abstractions;
using Messenger.Domain.Support;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
using Messenger.Infrastructure.Persistence.Mongo.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>MongoDB <see cref="ISupportTicketStore"/> (collection <c>tickets</c>).</summary>
public sealed class MongoTicketStore(MongoDbContext context) : ISupportTicketStore
{
    public const string CollectionName = "tickets";

    private IMongoCollection<TicketDocument> Collection =>
        context.Collection<TicketDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        // Agent queue: status triage, newest first.
        await keys.CreateOneAsync(
            new CreateIndexModel<TicketDocument>(
                Builders<TicketDocument>.IndexKeys.Ascending(d => d.Status).Descending(d => d.CreatedAt),
                new CreateIndexOptions { Name = "ix_status_created" }),
            cancellationToken: ct).ConfigureAwait(false);
        // "My tickets" for customers and agents.
        await keys.CreateOneAsync(
            new CreateIndexModel<TicketDocument>(
                Builders<TicketDocument>.IndexKeys.Ascending(d => d.CustomerId).Descending(d => d.CreatedAt),
                new CreateIndexOptions { Name = "ix_customer_created" }),
            cancellationToken: ct).ConfigureAwait(false);
        await keys.CreateOneAsync(
            new CreateIndexModel<TicketDocument>(
                Builders<TicketDocument>.IndexKeys.Ascending(d => d.AssigneeId).Descending(d => d.UpdatedAt),
                new CreateIndexOptions { Name = "ix_assignee_updated" }),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<SupportTicket?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : TicketMapper.ToDomain(doc);
    }

    public async Task<IReadOnlyList<SupportTicket>> ListAsync(
        TicketStatus? status, string? assigneeId, string? customerId, int limit, CancellationToken ct)
    {
        var builder = Builders<TicketDocument>.Filter;
        var filters = new List<FilterDefinition<TicketDocument>>();
        if (status.HasValue)
        {
            filters.Add(builder.Eq(d => d.Status, status.Value));
        }

        if (assigneeId is not null)
        {
            // Fail closed: a malformed id filter returns nothing, never the
            // unfiltered queue (the old code silently dropped the predicate).
            if (!ObjectId.TryParse(assigneeId, out _))
            {
                return [];
            }

            filters.Add(builder.Eq(d => d.AssigneeId, assigneeId));
        }

        if (customerId is not null)
        {
            if (!ObjectId.TryParse(customerId, out _))
            {
                return [];
            }

            filters.Add(builder.Eq(d => d.CustomerId, customerId));
        }

        var docs = await Collection.Find(filters.Count == 0 ? builder.Empty : builder.And(filters))
            .Sort(Builders<TicketDocument>.Sort.Descending(d => d.CreatedAt))
            .Limit(limit)
            .ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(TicketMapper.ToDomain).ToList();
    }

    public async Task AddAsync(SupportTicket ticket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        await Collection.InsertOneAsync(TicketMapper.ToDocument(ticket), cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(SupportTicket ticket, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var result = await Collection
            .ReplaceOneAsync(d => d.Id == ticket.Id, TicketMapper.ToDocument(ticket), cancellationToken: ct)
            .ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            throw new InvalidOperationException($"Ticket {ticket.Id} does not exist.");
        }
    }
}
