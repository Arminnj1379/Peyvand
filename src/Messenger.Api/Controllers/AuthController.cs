using Messenger.Api.Extensions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Auth;
using Messenger.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Messenger.Api.Controllers;

public sealed record RegisterRequest(
    string Username, string DisplayName, string Password, string? Phone, string? DeviceLabel);

public sealed record LoginRequest(string Login, string Password, string? DeviceLabel);

public sealed record RefreshRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);

/// <summary>Registration, login, refresh, logout, sessions, password change.</summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService auth) : ControllerBase
{
    [HttpPost("register")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Register([FromBody] RegisterRequest req, CancellationToken ct)
    {
        var result = await auth.RegisterAsync(new RegisterCustomerCommand(
            req.Username, req.DisplayName, req.Password, req.Phone,
            req.DeviceLabel, HttpContext.ClientIp()), ct).ConfigureAwait(false);
        return ResultMapper.ToHttp(result, 201);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> Login([FromBody] LoginRequest req, CancellationToken ct)
    {
        var result = await auth.LoginAsync(new LoginCommand(
            req.Login, req.Password, req.DeviceLabel, HttpContext.ClientIp()), ct).ConfigureAwait(false);
        return ResultMapper.ToHttp(result);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth")]
    [ProducesResponseType(typeof(AuthResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> Refresh([FromBody] RefreshRequest req, CancellationToken ct)
    {
        var result = await auth.RefreshAsync(new RefreshSessionCommand(req.RefreshToken, HttpContext.ClientIp()), ct)
            .ConfigureAwait(false);
        return ResultMapper.ToHttp(result);
    }

    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> Logout(CancellationToken ct) =>
        ResultMapper.ToHttp(await auth.LogoutAsync(User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPost("logout-all")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> LogoutAll(CancellationToken ct) =>
        ResultMapper.ToHttp(await auth.LogoutAllAsync(User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("sessions")]
    [Authorize]
    [ProducesResponseType(typeof(IReadOnlyList<SessionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> ListSessions(CancellationToken ct) =>
        ResultMapper.ToHttp(await auth.ListSessionsAsync(User.ToCaller(), ct).ConfigureAwait(false));

    [HttpDelete("sessions/{id}")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> RevokeSession([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await auth.RevokeSessionAsync(new RevokeSessionCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpPost("change-password")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> ChangePassword([FromBody] ChangePasswordRequest req, CancellationToken ct) =>
        ResultMapper.ToHttp(await auth.ChangePasswordAsync(
            new ChangePasswordCommand(req.CurrentPassword, req.NewPassword), User.ToCaller(), ct)
            .ConfigureAwait(false));
}
