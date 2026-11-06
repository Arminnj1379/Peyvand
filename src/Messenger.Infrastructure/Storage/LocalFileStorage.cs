using Messenger.Application.Abstractions;

namespace Messenger.Infrastructure.Storage;

/// <summary>
/// Development-only <see cref="IFileStorage"/> on local disk (no MinIO needed).
/// Layout mirrors object keys (<c>files/{id}/{safeName}</c>) with a
/// <c>.contenttype</c> sidecar; object keys are server-generated, but paths are
/// still confined to the root as defense in depth. Never used in Production.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string rootPath)
    {
        _root = Path.GetFullPath(
            string.IsNullOrWhiteSpace(rootPath)
                ? Path.Combine(AppContext.BaseDirectory, "data", "media")
                : rootPath);
    }

    public Task EnsureBucketAsync(CancellationToken ct)
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public async Task PutAsync(string objectKey, string contentType, byte[] bytes, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        ArgumentNullException.ThrowIfNull(bytes);
        var path = PathFor(objectKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, bytes, ct).ConfigureAwait(false);
        await File.WriteAllTextAsync(path + ".contenttype", contentType ?? "application/octet-stream", ct)
            .ConfigureAwait(false);
    }

    public async Task<(byte[] Bytes, string ContentType, long SizeBytes)?> GetAsync(
        string objectKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var path = PathFor(objectKey);
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        var sidecar = path + ".contenttype";
        var contentType = File.Exists(sidecar)
            ? await File.ReadAllTextAsync(sidecar, ct).ConfigureAwait(false)
            : "application/octet-stream";
        return (bytes, contentType, bytes.Length);
    }

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objectKey);
        var path = PathFor(objectKey);
        TryDelete(path);
        TryDelete(path + ".contenttype");
        return Task.CompletedTask;
    }

    private string PathFor(string objectKey)
    {
        var relative = objectKey.Replace('/', Path.DirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid object key.", nameof(objectKey));
        }

        return full;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Metadata is the source of truth; orphan bytes are swept by retention.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above: never fail the request on best-effort cleanup.
        }
    }
}
