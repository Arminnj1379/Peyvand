namespace Messenger.Application.Abstractions;

/// <summary>
/// Object bytes port (S3-compatible: MinIO in production). Metadata stays in
/// MongoDB; binaries never touch document storage. Implementations must use
/// server-generated object keys (see <c>FileValidation.BuildObjectKey</c>).
/// </summary>
public interface IFileStorage
{
    Task PutAsync(string objectKey, string contentType, byte[] bytes, CancellationToken ct);
    Task<(byte[] Bytes, string ContentType, long SizeBytes)?> GetAsync(string objectKey, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
    Task EnsureBucketAsync(CancellationToken ct);
}
