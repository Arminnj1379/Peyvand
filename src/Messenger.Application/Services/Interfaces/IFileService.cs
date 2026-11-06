using Messenger.Application.Features.Files;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Attachment upload/download/delete (upload-then-attach on send).</summary>
public interface IFileService
{
    Task<Result<FileDto>> UploadFileAsync(UploadFileCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<FileDownloadDto>> DownloadFileAsync(DownloadFileQuery query, CallerContext caller, CancellationToken ct);

    Task<Result> DeleteFileAsync(DeleteFileCommand command, CallerContext caller, CancellationToken ct);
}
