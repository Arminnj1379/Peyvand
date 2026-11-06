using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace Messenger.Infrastructure.Storage;

/// <summary>
/// Filesystem storage reachability: the root must exist and accept a
/// write/delete probe (catches read-only mounts and wrong ownership —
/// the failure mode that surfaces as 503 on uploads).
/// Tagged <c>storage</c>, same as the MinIO check it replaces.
/// </summary>
public sealed class LocalStorageHealthCheck(IOptions<FileStorageOptions> options) : IHealthCheck
{
    private readonly string _root = string.IsNullOrWhiteSpace(options.Value.RootPath)
        ? Path.Combine(AppContext.BaseDirectory, "data", "media")
        : options.Value.RootPath;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            Directory.CreateDirectory(_root);
            var probe = Path.Combine(_root, ".health");
            await File.WriteAllTextAsync(probe, "ok", cancellationToken).ConfigureAwait(false);
            File.Delete(probe);
            return HealthCheckResult.Healthy("Local disk storage (writable).");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Local disk storage is not writable.", ex);
        }
    }
}
