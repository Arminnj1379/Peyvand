using System.Net.Sockets;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Storage;

/// <summary>
/// Object-storage reachability (TCP connect to the S3 endpoint, 3 s budget).
/// Tagged <c>storage</c> — informational only, never gates readiness: the API
/// degrades gracefully (503 on uploads) when MinIO is down.
/// </summary>
public sealed class MinioHealthCheck(IOptions<MinioOptions> options) : IHealthCheck
{
    private readonly MinioOptions _options = options.Value;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            var endpoint = _options.Endpoint.Split(':');
            var host = endpoint[0];
            var port = endpoint.Length > 1 && int.TryParse(endpoint[1], out var parsed) ? parsed : 9000;
            using var client = new TcpClient();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            await client.ConnectAsync(host, port, linked.Token).ConfigureAwait(false);
            return HealthCheckResult.Healthy("Object storage reachable.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Object storage unreachable.", ex);
        }
    }
}
