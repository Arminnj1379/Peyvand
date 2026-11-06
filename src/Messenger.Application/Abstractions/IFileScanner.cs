namespace Messenger.Application.Abstractions;

/// <summary>
/// Malware scanning integration point. Runs inline on upload in MVP
/// (Infrastructure <c>NoOpFileScanner</c>: always Clean). Swap in a ClamAV/ICAP
/// adapter here without touching handlers: they only branch on the verdict.
/// </summary>
public enum ScanVerdict
{
    Clean = 1,
    Infected = 2
}

public interface IFileScanner
{
    Task<ScanVerdict> ScanAsync(byte[] bytes, string contentType, CancellationToken ct);
}
