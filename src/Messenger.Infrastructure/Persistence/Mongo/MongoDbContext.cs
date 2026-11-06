using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo;

/// <summary>
/// Thin MongoDB accessor. Per-aggregate stores (e.g. <c>MongoChatStore</c>)
/// are added in Phase 1+ and use <see cref="Database"/> directly —
/// no generic repositories (ADR-04).
/// </summary>
public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly ILogger<MongoDbContext> _logger;

    public MongoDbContext(IMongoClient client, IOptions<MongoOptions> options, ILogger<MongoDbContext> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;
        _database = client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoDatabase Database => _database;

    public IMongoCollection<T> Collection<T>(string name) => _database.GetCollection<T>(name);

    /// <summary>
    /// Connectivity probe used by <c>/readyz</c>. Per-collection index creation
    /// from database-design.md is added alongside each Phase 1+ store.
    /// </summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken)
    {
        using var cursor = await _database.ListCollectionNamesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var names = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MongoDB database ready. Collections: {Count}", names.Count);
    }
}
