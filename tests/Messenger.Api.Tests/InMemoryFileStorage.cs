using Messenger.Application.Abstractions;

namespace Messenger.Api.Tests;

/// <summary>
/// In-memory <see cref="IFileStorage"/> for API tests: same S3 semantics as
/// MinIO (put/get/delete by server-generated key) without a live server.
/// The MinIO adapter itself is covered by conditional live tests
/// (<c>MinioFileStorageLiveTests</c>) wherever MinIO is reachable.
/// </summary>
internal sealed class InMemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, (byte[] Bytes, string ContentType)> _objects = [];

    public Task PutAsync(string objectKey, string contentType, byte[] bytes, CancellationToken ct)
    {
        _objects[objectKey] = (bytes, contentType);
        return Task.CompletedTask;
    }

    public Task<(byte[] Bytes, string ContentType, long SizeBytes)?> GetAsync(string objectKey, CancellationToken ct) =>
        Task.FromResult(_objects.TryGetValue(objectKey, out var o)
            ? ((byte[], string, long)?)(o.Bytes, o.ContentType, o.Bytes.Length)
            : null);

    public Task DeleteAsync(string objectKey, CancellationToken ct)
    {
        _objects.Remove(objectKey);
        return Task.CompletedTask;
    }

    public Task EnsureBucketAsync(CancellationToken ct) => Task.CompletedTask;

    public void Clear() => _objects.Clear();
}
