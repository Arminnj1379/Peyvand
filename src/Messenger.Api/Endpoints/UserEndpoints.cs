using MediatR;
using Messenger.Application.Features.Users;
using Messenger.Application.Users;

namespace Messenger.Api.Endpoints;

public sealed record UpdateProfileRequest(string DisplayName, string? Bio);

public sealed record UpdateSettingsRequest(string Theme, bool NotificationsEnabled);

/// <summary>Self profile, directory, avatars, blocking.</summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/users").WithTags("Users").RequireAuthorization();

        group.MapGet("/me", async (ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetMyProfileQuery(), ct).ConfigureAwait(false)))
        .WithName("GetMyProfile")
        .Produces<UserDto>()
        .ProducesProblem(401);

        group.MapPatch("/me", async (UpdateProfileRequest req, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new UpdateMyProfileCommand(req.DisplayName, req.Bio), ct)
                .ConfigureAwait(false)))
        .WithName("UpdateMyProfile")
        .Produces<UserDto>()
        .ProducesProblem(400)
        .ProducesProblem(401);

        group.MapPost("/me/avatar", async (IFormFile file, ISender sender, CancellationToken ct) =>
        {
            if (file.Length == 0)
            {
                return Results.Problem(title: "No file uploaded.", statusCode: 400);
            }

            if (file.Length > UploadAvatarCommand.MaxBytes)
            {
                return ResultMapper.Problem(Domain.Users.UserErrors.AvatarTooLarge);
            }

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream, ct).ConfigureAwait(false);
            var result = await sender.Send(
                new UploadAvatarCommand(stream.ToArray(), file.ContentType), ct).ConfigureAwait(false);
            return ResultMapper.ToHttp(result);
        })
        .DisableAntiforgery()
        .WithName("UploadMyAvatar")
        .WithSummary("Uploads the caller's avatar (JPEG/PNG/WebP, max 2 MB).")
        .Produces<UserDto>()
        .ProducesProblem(400)
        .ProducesProblem(401);

        group.MapDelete("/me/avatar", async (ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new RemoveAvatarCommand(), ct).ConfigureAwait(false)))
        .WithName("RemoveMyAvatar")
        .Produces<UserDto>()
        .ProducesProblem(401);

        group.MapGet("/me/settings", async (ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetMySettingsQuery(), ct).ConfigureAwait(false)))
        .WithName("GetMySettings")
        .WithSummary("Caller preferences: UI theme and notification master switch.")
        .Produces<SettingsDto>()
        .ProducesProblem(401);

        group.MapPatch("/me/settings", async (UpdateSettingsRequest req, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new UpdateMySettingsCommand(req.Theme, req.NotificationsEnabled), ct)
                    .ConfigureAwait(false)))
        .WithName("UpdateMySettings")
        .Produces<SettingsDto>()
        .ProducesProblem(400)
        .ProducesProblem(401);

        group.MapGet("/search", async (string q, int limit, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new SearchUsersQuery(q, limit == 0 ? 20 : limit), ct)
                .ConfigureAwait(false)))
        .WithName("SearchUsers")
        .Produces<IReadOnlyList<UserDto>>()
        .ProducesProblem(400)
        .ProducesProblem(401);

        group.MapGet("/{id}", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetUserByIdQuery(id), ct).ConfigureAwait(false)))
        .WithName("GetUserById")
        .Produces<UserDto>()
        .ProducesProblem(401)
        .ProducesProblem(404);

        group.MapGet("/{id}/avatar", async (string id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetAvatarQuery(id), ct).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Bytes(result.Value!.Bytes, result.Value.ContentType)
                : ResultMapper.Problem(result.Error);
        })
        .WithName("GetAvatar")
        .WithSummary("Serves stored avatar bytes (authorized fetch; same visibility as profiles).")
        .Produces(200, contentType: "image/jpeg")
        .ProducesProblem(401)
        .ProducesProblem(404);

        group.MapPost("/{id}/block", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new BlockUserCommand(id), ct).ConfigureAwait(false)))
        .WithName("BlockUser")
        .Produces(200)
        .ProducesProblem(401)
        .ProducesProblem(404);

        group.MapDelete("/{id}/block", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new UnblockUserCommand(id), ct).ConfigureAwait(false)))
        .WithName("UnblockUser")
        .Produces(200)
        .ProducesProblem(401);

        return app;
    }
}
