using System.Security.Cryptography;
using Messenger.Domain.Common;

namespace Messenger.Domain.Files;

/// <summary>
/// Uploaded file aggregate. Binaries live in S3-compatible object storage;
/// MongoDB holds this metadata only (collection <c>files</c>).
/// A file is uploaded unattached, then linked to exactly one message;
/// forwards reference the same bytes without re-attaching.
/// </summary>
public sealed class ChatFile : Entity
{
    private ChatFile(
        string id,
        string ownerId,
        string fileName,
        string contentType,
        long sizeBytes,
        string sha256,
        FileKind kind,
        string objectKey,
        DateTime now) : base(id)
    {
        OwnerId = ownerId;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        Kind = kind;
        Status = FileStatus.Ready;
        ObjectKey = objectKey;
        CreatedAt = now;
    }

    public string OwnerId { get; }
    public string? ChatId { get; private set; }
    public string? MessageId { get; private set; }
    public string FileName { get; }
    public string ContentType { get; }
    public long SizeBytes { get; }
    public string Sha256 { get; }
    public FileKind Kind { get; }
    public FileStatus Status { get; private set; }
    public string ObjectKey { get; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? AttachedAt { get; private set; }

    public bool IsAttached => MessageId is not null;

    public static Result<ChatFile> Create(
        string id,
        string ownerId,
        byte[] bytes,
        string fileName,
        string contentType,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        ArgumentNullException.ThrowIfNull(bytes);

        var validated = FileValidation.Validate(bytes, fileName, contentType);
        if (validated.IsFailure)
        {
            return Result<ChatFile>.Fail(validated.Error);
        }

        var kind = validated.Value;
        var safeName = FileValidation.SanitizeFileName(fileName);
        var file = new ChatFile(
            id,
            ownerId,
            safeName,
            FileValidation.NormalizeContentType(contentType),
            bytes.Length,
            Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            kind,
            FileValidation.BuildObjectKey(id, safeName),
            now);
        return Result<ChatFile>.Ok(file);
    }

    /// <summary>Links the file to its message. One attachment target per file.</summary>
    public Result Attach(string chatId, string messageId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (Status != FileStatus.Ready)
        {
            return Result.Fail(FileErrors.NotReady);
        }

        if (IsAttached)
        {
            return Result.Fail(FileErrors.AlreadyAttached);
        }

        ChatId = chatId;
        MessageId = messageId;
        AttachedAt = now;
        return Result.Ok();
    }

    public void MarkRejected()
    {
        Status = FileStatus.Rejected;
    }

    /// <summary>
    /// Persistence rehydration. Bypasses <see cref="Create"/> (no re-validation
    /// of already-stored state). Infrastructure use only.
    /// </summary>
    public static ChatFile Rehydrate(FileSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var file = new ChatFile(
            snapshot.Id, snapshot.OwnerId, snapshot.FileName, snapshot.ContentType,
            snapshot.SizeBytes, snapshot.Sha256, snapshot.Kind, snapshot.ObjectKey,
            snapshot.CreatedAt);
        file.ChatId = snapshot.ChatId;
        file.MessageId = snapshot.MessageId;
        file.Status = snapshot.Status;
        file.AttachedAt = snapshot.AttachedAt;
        return file;
    }
}
