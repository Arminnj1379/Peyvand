using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
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

public sealed record UploadFileCommand(byte[] Bytes, string FileName, string ContentType)
    : IRequest<Result<FileDto>>;

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

public sealed class UploadFileHandler(
    IFileStore files,
    IFileStorage storage,
    IFileScanner scanner,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<UploadFileCommand, Result<FileDto>>
{
    public async Task<Result<FileDto>> Handle(UploadFileCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<FileDto>.Fail(AuthErrors.Unauthenticated);
        }

        var created = ChatFile.Create(
            ids.NewId(), currentUser.UserId, request.Bytes,
            request.FileName, request.ContentType, clock.UtcNow);
        if (created.IsFailure)
        {
            return Result<FileDto>.Fail(created.Error);
        }

        // Malware scan integration point: reject before any byte is stored.
        var verdict = await scanner.ScanAsync(request.Bytes, created.Value!.ContentType, ct)
            .ConfigureAwait(false);
        if (verdict == ScanVerdict.Infected)
        {
            return Result<FileDto>.Fail(FileErrors.ScanRejected);
        }

        try
        {
            await storage.EnsureBucketAsync(ct).ConfigureAwait(false);
            await storage.PutAsync(
                created.Value.ObjectKey, created.Value.ContentType, request.Bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<FileDto>.Fail(FileErrors.StorageUnavailable);
        }

        await files.AddAsync(created.Value, ct).ConfigureAwait(false);
        return Result<FileDto>.Ok(FileAccess.ToFileDto(created.Value));
    }
}

public sealed record DownloadFileQuery(string FileId) : IRequest<Result<FileDownloadDto>>;

public sealed class DownloadFileValidator : AbstractValidator<DownloadFileQuery>
{
    public DownloadFileValidator()
    {
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(64);
    }
}

public sealed class DownloadFileHandler(
    IFileStore files,
    IFileStorage storage,
    IChatStore chats,
    ICurrentUser currentUser) : IRequestHandler<DownloadFileQuery, Result<FileDownloadDto>>
{
    public async Task<Result<FileDownloadDto>> Handle(DownloadFileQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<FileDownloadDto>.Fail(AuthErrors.Unauthenticated);
        }

        var file = await files.GetByIdAsync(request.FileId, ct).ConfigureAwait(false);
        var authorized = await FileAccess.RequireDownloadableAsync(chats, file, currentUser.UserId, ct)
            .ConfigureAwait(false);
        if (authorized.IsFailure)
        {
            return Result<FileDownloadDto>.Fail(authorized.Error);
        }

        byte[] bytes;
        try
        {
            var stored = await storage.GetAsync(authorized.Value!.ObjectKey, ct).ConfigureAwait(false);
            if (stored is null)
            {
                return Result<FileDownloadDto>.Fail(FileErrors.NotFound);
            }

            bytes = stored.Value.Bytes;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<FileDownloadDto>.Fail(FileErrors.StorageUnavailable);
        }

        var f = authorized.Value;
        return Result<FileDownloadDto>.Ok(new FileDownloadDto(f.FileName, f.ContentType, f.SizeBytes, bytes));
    }
}

public sealed record DeleteFileCommand(string FileId) : IRequest<Result>;

public sealed class DeleteFileValidator : AbstractValidator<DeleteFileCommand>
{
    public DeleteFileValidator()
    {
        RuleFor(x => x.FileId).NotEmpty().MaximumLength(64);
    }
}

/// <summary>Owners may delete pending uploads (cancel/abandon). Attached files are
/// immutable — they follow message retention, not per-file deletes.</summary>
public sealed class DeleteFileHandler(
    IFileStore files,
    IFileStorage storage,
    ICurrentUser currentUser) : IRequestHandler<DeleteFileCommand, Result>
{
    public async Task<Result> Handle(DeleteFileCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var file = await files.GetByIdAsync(request.FileId, ct).ConfigureAwait(false);
        if (file is null)
        {
            return Result.Fail(FileErrors.NotFound);
        }

        if (file.OwnerId != currentUser.UserId)
        {
            return Result.Fail(FileErrors.NotOwner);
        }

        if (file.IsAttached)
        {
            return Result.Fail(FileErrors.AlreadyAttached);
        }

        await files.DeleteAsync(file.Id, ct).ConfigureAwait(false);
        try
        {
            await storage.DeleteAsync(file.ObjectKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Metadata is gone (source of truth); orphan bytes are swept by retention.
        }

        return Result.Ok();
    }
}
