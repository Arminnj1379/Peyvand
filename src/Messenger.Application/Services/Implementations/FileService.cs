using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Files;
using Messenger.Application.Services.Interfaces;
using Messenger.Domain.Common;
using Messenger.Domain.Files;

namespace Messenger.Application.Services.Implementations;

/// <summary>Attachment use cases. Logic moved verbatim from the former MediatR handlers.</summary>
public sealed class FileService(
    IFileStore files,
    IFileStorage storage,
    IFileScanner scanner,
    IChatStore chats,
    IIdGenerator ids,
    IClock clock) : IFileService
{
    public async Task<Result<FileDto>> UploadFileAsync(
        UploadFileCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UploadFileValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<FileDto>.Fail(AuthErrors.Unauthenticated);
        }

        var created = ChatFile.Create(
            ids.NewId(), caller.UserId, command.Bytes,
            command.FileName, command.ContentType, clock.UtcNow);
        if (created.IsFailure)
        {
            return Result<FileDto>.Fail(created.Error);
        }

        // Malware scan integration point: reject before any byte is stored.
        var verdict = await scanner.ScanAsync(command.Bytes, created.Value!.ContentType, ct)
            .ConfigureAwait(false);
        if (verdict == ScanVerdict.Infected)
        {
            return Result<FileDto>.Fail(FileErrors.ScanRejected);
        }

        try
        {
            await storage.EnsureBucketAsync(ct).ConfigureAwait(false);
            await storage.PutAsync(
                created.Value.ObjectKey, created.Value.ContentType, command.Bytes, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result<FileDto>.Fail(FileErrors.StorageUnavailable);
        }

        await files.AddAsync(created.Value, ct).ConfigureAwait(false);
        return Result<FileDto>.Ok(Features.Files.FileAccess.ToFileDto(created.Value));
    }

    public async Task<Result<FileDownloadDto>> DownloadFileAsync(
        DownloadFileQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new DownloadFileValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<FileDownloadDto>.Fail(AuthErrors.Unauthenticated);
        }

        var file = await files.GetByIdAsync(query.FileId, ct).ConfigureAwait(false);
        var authorized = await Features.Files.FileAccess.RequireDownloadableAsync(chats, file, caller.UserId, ct)
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

    public async Task<Result> DeleteFileAsync(
        DeleteFileCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new DeleteFileValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var file = await files.GetByIdAsync(command.FileId, ct).ConfigureAwait(false);
        if (file is null)
        {
            return Result.Fail(FileErrors.NotFound);
        }

        if (file.OwnerId != caller.UserId)
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
