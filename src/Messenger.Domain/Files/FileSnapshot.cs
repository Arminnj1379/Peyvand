namespace Messenger.Domain.Files;

/// <summary>Full stored state of a <see cref="ChatFile"/> for rehydration. Infrastructure use only.</summary>
public sealed record FileSnapshot(
    string Id,
    string OwnerId,
    string? ChatId,
    string? MessageId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string Sha256,
    FileKind Kind,
    FileStatus Status,
    string ObjectKey,
    DateTime CreatedAt,
    DateTime? AttachedAt);
