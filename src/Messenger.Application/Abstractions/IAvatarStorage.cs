namespace Messenger.Application.Abstractions;

/// <summary>
/// Avatar byte storage. MongoDB-backed until the Media/MinIO slice lands;
/// the seam keeps that swap internal to Infrastructure.
/// </summary>
public sealed record AvatarReference(string ObjectKey, int SizeBytes);

public interface IAvatarStorage
{
    Task<AvatarReference> SaveAsync(string userId, string contentType, byte[] bytes, CancellationToken ct);
    Task<(byte[] Bytes, string ContentType)?> LoadAsync(string objectKey, CancellationToken ct);
    Task DeleteAsync(string objectKey, CancellationToken ct);
}
