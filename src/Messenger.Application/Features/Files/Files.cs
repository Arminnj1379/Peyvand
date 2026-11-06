using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Common;
using Messenger.Domain.Files;

namespace Messenger.Application.Features.Files;

/// <summary>Upload metadata returned to clients (upload first, attach on send).</summary>
public sealed record FileDto(
    string Id,
    string FileName,
    string Kind,
    string ContentType,
    long SizeBytes,
    string Sha256,
    DateTime CreatedAt);

/// <summary>Authorized download payload. Bytes come from object storage, never MongoDB.</summary>
public sealed record FileDownloadDto(
    string FileName,
    string ContentType,
    long SizeBytes,
    byte[] Bytes);

/// <summary>Single home of file authorization. Object keys never leave the server.</summary>
internal static class FileAccess
{
    public static AttachmentDto ToAttachmentDto(ChatFile file) =>
        new(file.Id, file.FileName, file.Kind.ToString(), file.ContentType,
            file.SizeBytes, $"/api/v1/files/{file.Id}");

    public static FileDto ToFileDto(ChatFile file) =>
        new(file.Id, file.FileName, file.Kind.ToString(), file.ContentType,
            file.SizeBytes, file.Sha256, file.CreatedAt);

    /// <summary>
    /// Who may fetch bytes: chat members (attached files) or the uploader
    /// (pending uploads, e.g. preview-before-send). Everyone else gets NotFound
    /// (neutral — no existence leak).
    /// </summary>
    public static async Task<Result<ChatFile>> RequireDownloadableAsync(
        IChatStore chats,
        ChatFile? file,
        string callerId,
        CancellationToken ct)
    {
        if (file is null || file.Status != FileStatus.Ready)
        {
            return Result<ChatFile>.Fail(FileErrors.NotFound);
        }

        if (file.ChatId is null)
        {
            return file.OwnerId == callerId
                ? Result<ChatFile>.Ok(file)
                : Result<ChatFile>.Fail(FileErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(file.ChatId, ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || !chat.IsMember(callerId))
        {
            return Result<ChatFile>.Fail(FileErrors.NotFound);
        }

        return Result<ChatFile>.Ok(file);
    }
}

public sealed record UploadFileCommand(byte[] Bytes, string FileName, string ContentType);

public sealed class UploadFileValidator : AbstractValidator<UploadFileCommand>
{
    public UploadFileValidator()
    {
        RuleFor(x => x.Bytes).NotEmpty().Must(b => b.Length <= FileValidation.MaxVideoBytes)
            .WithMessage("File is larger than the maximum supported size (100 MB).");
        RuleFor(x => x.FileName).NotEmpty().MaximumLength(256);
        RuleFor(x => x.ContentType).NotEmpty().MaximumLength(128);
    }
}

public sealed record DownloadFileQuery(string FileId);

public sealed class DownloadFileValidator : AbstractValidator<DownloadFileQuery>
{
    public DownloadFileValidator()
    {
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(64);
    }
}

public sealed record DeleteFileCommand(string FileId);

public sealed class DeleteFileValidator : AbstractValidator<DeleteFileCommand>
{
    public DeleteFileValidator()
    {
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(64);
    }
}

/// <summary>Owners may delete pending uploads (cancel/abandon). Attached files are
/// immutable — they follow message retention, not per-file deletes.</summary>
