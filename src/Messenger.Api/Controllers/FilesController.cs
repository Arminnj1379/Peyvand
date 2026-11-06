using Messenger.Api.Extensions;
using Messenger.Application.Features.Files;
using Messenger.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

/// <summary>
/// Media attachments. Upload-then-attach: clients upload bytes first (progress
/// via the multipart POST), then reference the file id when sending a message.
/// Downloads proxy through the API with authorization — object keys never leave
/// the server and MinIO is never exposed to clients.
/// </summary>
[ApiController]
[Route("api/v1/files")]
[Authorize]
public sealed class FilesController(IFileService files) : ControllerBase
{
    [HttpPost("")]
    [IgnoreAntiforgeryToken]
    [DisableRequestSizeLimit]
    [ProducesResponseType(typeof(FileDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> UploadFile(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return Results.Problem(title: "No file uploaded.", statusCode: 400);
        }

        // Multipart gives us the client file name + declared type; the service
        // re-validates everything against the actual bytes.
        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct).ConfigureAwait(false);
        var result = await files.UploadFileAsync(
            new UploadFileCommand(stream.ToArray(), file.FileName, file.ContentType),
            User.ToCaller(), ct).ConfigureAwait(false);
        return ResultMapper.ToHttp(result, 201);
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> DownloadFile(
        [FromRoute] string id, [FromQuery] bool download = false, CancellationToken ct = default)
    {
        var result = await files.DownloadFileAsync(new DownloadFileQuery(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsFailure)
        {
            return ResultMapper.Problem(result.Error);
        }

        var file = result.Value!;
        // Range processing on: video/audio seek without full re-downloads.
        // ?download=1 forces a save dialog; inline otherwise (preview in chat).
        return Results.Bytes(
            file.Bytes, file.ContentType, download ? file.FileName : null,
            enableRangeProcessing: true);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> DeleteFile([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await files.DeleteFileAsync(new DeleteFileCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false));
}
