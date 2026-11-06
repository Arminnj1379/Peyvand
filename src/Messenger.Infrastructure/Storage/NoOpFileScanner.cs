using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Storage;

/// <summary>
/// Malware scanning placeholder. Always Clean: swap for a ClamAV/ICAP adapter
/// implementing <see cref="IFileScanner"/> — handlers branch only on the verdict,
/// so no call-site changes are needed.
/// </summary>
public sealed class NoOpFileScanner : IFileScanner
{
    public Task<ScanVerdict> ScanAsync(byte[] bytes, string contentType, CancellationToken ct) =>
        Task.FromResult(ScanVerdict.Clean);
}
