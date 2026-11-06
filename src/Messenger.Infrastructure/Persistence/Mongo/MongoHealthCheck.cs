using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Messenger.Infrastructure.Persistence.Mongo;

/// <summary>Liveness of the MongoDB dependency for the <c>ready</c> tag.</summary>
public sealed class MongoHealthCheck(MongoDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            IMongoDatabase database = dbContext.Database;
            await database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1), cancellationToken: cancellationToken).ConfigureAwait(false);
            return HealthCheckResult.Healthy("MongoDB reachable.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MongoDB unreachable.", ex);
        }
    }
}
