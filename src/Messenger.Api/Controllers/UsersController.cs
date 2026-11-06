using Messenger.Api.Extensions;
using Messenger.Application.Features.Users;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

public sealed record UpdateProfileRequest(string DisplayName, string? Bio);

public sealed record UpdateSettingsRequest(string Theme, bool NotificationsEnabled);

/// <summary>Self profile, directory, avatars, blocking.</summary>
[ApiController]
[Route("api/v1/users")]
[Authorize]
public sealed class UsersController(IUserService users) : ControllerBase
{
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> GetMyProfile(CancellationToken ct) =>
        ResultMapper.ToHttp(await users.GetMyProfileAsync(User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPatch("me")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> UpdateMyProfile([FromBody] UpdateProfileRequest req, CancellationToken ct) =>
        ResultMapper.ToHttp(await users.UpdateMyProfileAsync(
            new UpdateMyProfileCommand(req.DisplayName, req.Bio), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPost("me/avatar")]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> UploadMyAvatar(IFormFile file, CancellationToken ct)
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
        var result = await users.UploadAvatarAsync(
            new UploadAvatarCommand(stream.ToArray(), file.ContentType), User.ToCaller(), ct)
            .ConfigureAwait(false);
        return ResultMapper.ToHttp(result);
    }

    [HttpDelete("me/avatar")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> RemoveMyAvatar(CancellationToken ct) =>
        ResultMapper.ToHttp(await users.RemoveAvatarAsync(User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("me/settings")]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> GetMySettings(CancellationToken ct) =>
        ResultMapper.ToHttp(await users.GetMySettingsAsync(User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPatch("me/settings")]
    [ProducesResponseType(typeof(SettingsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> UpdateMySettings([FromBody] UpdateSettingsRequest req, CancellationToken ct) =>
        ResultMapper.ToHttp(await users.UpdateMySettingsAsync(
            new UpdateMySettingsCommand(req.Theme, req.NotificationsEnabled), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> SearchUsers([FromQuery] string q, [FromQuery] int limit = 0, CancellationToken ct = default) =>
        ResultMapper.ToHttp(await users.SearchUsersAsync(
            new SearchUsersQuery(q, limit == 0 ? 20 : limit), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetUserById([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await users.GetUserByIdAsync(new GetUserByIdQuery(id), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpGet("{id}/avatar")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetAvatar([FromRoute] string id, CancellationToken ct)
    {
        var result = await users.GetAvatarAsync(new GetAvatarQuery(id), User.ToCaller(), ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Bytes(result.Value!.Bytes, result.Value.ContentType)
            : ResultMapper.Problem(result.Error);
    }

    [HttpPost("{id}/block")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> BlockUser([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await users.BlockUserAsync(new BlockUserCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpDelete("{id}/block")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> UnblockUser([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await users.UnblockUserAsync(new UnblockUserCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false));
}
