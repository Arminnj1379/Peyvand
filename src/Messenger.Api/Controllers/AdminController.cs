using Messenger.Api.Auth;
using Messenger.Api.Extensions;
using Messenger.Application.Features.Admin;
using Messenger.Application.Features.Auth;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

public sealed record CreateEmployeeRequest(
    string Username, string DisplayName, string Password, IReadOnlyList<string> Roles, string? Phone);

public sealed record SetActiveRequest(bool IsActive);

public sealed record SetRolesRequest(IReadOnlyList<string> Roles);

/// <summary>Administrator user management (Admin policy + service re-checks).</summary>
[ApiController]
[Route("api/v1/admin/users")]
[Authorize(Policy = JwtSetup.AdminPolicy)]
public sealed class AdminController(IAdminService admin, ILogger<AdminAudit> audit) : ControllerBase
{
    [HttpPost("")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CreateEmployee([FromBody] CreateEmployeeRequest req, CancellationToken ct)
    {
        var result = await admin.CreateEmployeeAsync(new CreateEmployeeCommand(
            req.Username, req.DisplayName, req.Password, req.Roles, req.Phone), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            audit.LogInformation("Employee {UserId} created by {Actor}.", result.Value!.Id, User.ActorId());
        }

        return ResultMapper.ToHttp(result, 201);
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<UserDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> ListUsers(
        [FromQuery] string? userType,
        [FromQuery] string? role,
        [FromQuery] bool? isActive,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 0,
        CancellationToken ct = default) =>
        ResultMapper.ToHttp(await admin.ListUsersAsync(new ListUsersQuery(
            userType, role, isActive, cursor, limit == 0 ? 20 : limit), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpPatch("{id}/active")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> SetUserActive(
        [FromRoute] string id, [FromBody] SetActiveRequest req, CancellationToken ct)
    {
        var result = await admin.SetUserActiveAsync(
            new SetUserActiveCommand(id, req.IsActive), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            audit.LogInformation("User {UserId} active={IsActive} set by {Actor}.", id, req.IsActive, User.ActorId());
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPut("{id}/roles")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> SetUserRoles(
        [FromRoute] string id, [FromBody] SetRolesRequest req, CancellationToken ct)
    {
        var result = await admin.SetUserRolesAsync(
            new SetUserRolesCommand(id, req.Roles), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            audit.LogInformation("User {UserId} roles set to [{Roles}] by {Actor}.",
                id, string.Join(',', req.Roles), User.ActorId());
        }

        return ResultMapper.ToHttp(result);
    }
}

/// <summary>Logger category for admin audit events. No PII in messages (ids only).</summary>
public sealed class AdminAudit;
