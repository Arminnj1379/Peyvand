using MediatR;
using Messenger.Application.Features.Files;
using Messenger.Domain.Files;

namespace Messenger.Api.Endpoints;

/// <summary>
/// Media attachments. Upload-then-attach: clients upload bytes first (progress
/// via the multipart POST), then reference the file id when sending a message.
/// Downloads proxy through the API with authorization — object keys never leave
/// the server and MinIO is never exposed to clients.
/// </summary>
public static class FileEndpoints
{
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/files").WithTags("Files").RequireAuthorization();

        group.MapPost("/", async (IFormFile file, ISender sender, CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.Problem(title: "No file uploaded.", statusCode: 400);
            }

            // Multipart gives us the client file name + declared type; the handler
            // re-validates everything against the actual bytes.
            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct).ConfigureAwait(false);
            var result = await sender.Send(
                new UploadFileCommand(
                    stream.ToArray(), file.FileName, file.ContentType), ct).ConfigureAwait(false);
            return ResultMapper.ToHttp(result, 201);
        })
        .DisableAntiforgery()
        // Videos may reach 100 MB (validated in the handler); Kestrel's 30 MB
        // default would reject them before validation runs.
        .WithMetadata(new Microsoft.AspNetCore.Mvc.DisableRequestSizeLimitAttribute())
        .WithName("UploadFile")
        .WithSummary("Uploads an attachment (images, video, audio, documents). Returns file metadata for attach-on-send.")
        .Produces<FileDto>(201)
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(409)
        .ProducesProblem(413)
        .ProducesProblem(422)
        .ProducesProblem(503);

        group.MapGet("/{id}", async (string id, ISender sender, CancellationToken ct, bool download = false) =>
        {
            var result = await sender.Send(new DownloadFileQuery(id), ct).ConfigureAwait(false);
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
        })
        .WithName("DownloadFile")
        .WithSummary("Downloads attachment bytes (chat members, or the uploader for pending files).")
        .Produces(200, contentType: "application/octet-stream")
        .ProducesProblem(404)
        .ProducesProblem(503);

        group.MapDelete("/{id}", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new DeleteFileCommand(id), ct).ConfigureAwait(false)))
        .WithName("DeleteFile")
        .WithSummary("Deletes a pending (unattached) upload. Attached files follow message retention.")
        .Produces(200)
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        return app;
    }
}
