using Messenger.Application.Abstractions;
using Messenger.Domain.Files;
using Messenger.Infrastructure.Persistence.Mongo.Documents;
using Messenger.Infrastructure.Persistence.Mongo.Mapping;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo.Stores;

/// <summary>MongoDB <see cref="IFileStore"/> (collection <c>files</c>). Metadata only.</summary>
public sealed class MongoFileStore(MongoDbContext context) : IFileStore
{
    public const string CollectionName = "files";

    private IMongoCollection<FileDocument> Collection =>
        context.Collection<FileDocument>(CollectionName);

    public async Task EnsureIndexesAsync(CancellationToken ct)
    {
        var keys = Collection.Indexes;
        // Attachment lookup: file backing a message.
        await keys.CreateOneAsync(
            new CreateIndexModel<FileDocument>(
                Builders<FileDocument>.IndexKeys.Ascending(d => d.MessageId),
                new CreateIndexOptions { Sparse = true, Name = "ix_message" }),
            cancellationToken: ct).ConfigureAwait(false);
        // Uploader's recent uploads (abandoned-upload cleanup).
        await keys.CreateOneAsync(
            new CreateIndexModel<FileDocument>(
                Builders<FileDocument>.IndexKeys.Ascending(d => d.OwnerId).Descending(d => d.CreatedAt),
                new CreateIndexOptions { Name = "ix_owner_created" }),
            cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task<ChatFile?> GetByIdAsync(string id, CancellationToken ct)
    {
        if (!ObjectId.TryParse(id, out _))
        {
            return null;
        }

        var doc = await Collection.Find(d => d.Id == id).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return doc is null ? null : FileMapper.ToDomain(doc);
    }

    public async Task<IReadOnlyList<ChatFile>> ListByIdsAsync(IReadOnlyList<string> ids, CancellationToken ct)
    {
        var valid = ids.Where(id => ObjectId.TryParse(id, out _)).ToList();
        if (valid.Count == 0)
        {
            return [];
        }

        var docs = await Collection.Find(d => valid.Contains(d.Id)).ToListAsync(ct).ConfigureAwait(false);
        return docs.Select(FileMapper.ToDomain).ToList();
    }

    public async Task AddAsync(ChatFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        await Collection.InsertOneAsync(FileMapper.ToDocument(file), cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateAsync(ChatFile file, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(file);
        var result = await Collection
            .ReplaceOneAsync(d => d.Id == file.Id, FileMapper.ToDocument(file), cancellationToken: ct)
            .ConfigureAwait(false);
        if (result.MatchedCount == 0)
        {
            throw new InvalidOperationException($"File {file.Id} does not exist.");
        }
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
