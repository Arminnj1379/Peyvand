using MediatR;
using Messenger.Api.Auth;
using Messenger.Application.Features.Admin;
using Messenger.Application.Features.Auth;
using Messenger.Application.Users;

namespace Messenger.Api.Endpoints;

public sealed record CreateEmployeeRequest(
    string Username, string DisplayName, string Password, IReadOnlyList<string> Roles, string? Phone);

public sealed record SetActiveRequest(bool IsActive);

public sealed record SetRolesRequest(IReadOnlyList<string> Roles);

/// <summary>Administrator user management (Admin policy + handler re-checks).</summary>
public static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/users")
            .WithTags("Admin")
            .RequireAuthorization(JwtSetup.AdminPolicy);

        group.MapPost("/", async (CreateEmployeeRequest req, ISender sender,
            HttpContext ctx, ILogger<AdminAudit> audit, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateEmployeeCommand(
                req.Username, req.DisplayName, req.Password, req.Roles, req.Phone), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                audit.LogInformation("Employee {UserId} created by {Actor}.", result.Value!.Id, ActorOf(ctx));
            }

            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("CreateEmployee")
        .WithSummary("Creates an internal employee account.")
        .Produces<UserDto>(201)
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(409);

        group.MapGet("/", async (
            string? userType, string? role, bool? isActive, string? cursor, int limit,
            ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new ListUsersQuery(
                userType, role, isActive, cursor, limit == 0 ? 20 : limit), ct).ConfigureAwait(false);
            return ResultMapper.ToHttp(result);
        })
        .WithName("ListUsers")
        .Produces<IReadOnlyList<UserDto>>()
        .ProducesProblem(400)
        .ProducesProblem(403);

        group.MapPatch("/{id}/active", async (string id, SetActiveRequest req, ISender sender,
            HttpContext ctx, ILogger<AdminAudit> audit, CancellationToken ct) =>
        {
            var result = await sender.Send(new SetUserActiveCommand(id, req.IsActive), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                audit.LogInformation("User {UserId} active={IsActive} set by {Actor}.", id, req.IsActive, ActorOf(ctx));
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("SetUserActive")
        .WithSummary("Activates/deactivates an account (deactivation revokes sessions).")
        .Produces<UserDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPut("/{id}/roles", async (string id, SetRolesRequest req, ISender sender,
            HttpContext ctx, ILogger<AdminAudit> audit, CancellationToken ct) =>
        {
            var result = await sender.Send(new SetUserRolesCommand(id, req.Roles), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                audit.LogInformation("User {UserId} roles set to [{Roles}] by {Actor}.",
                    id, string.Join(',', req.Roles), ActorOf(ctx));
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("SetUserRoles")
        .Produces<UserDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        return app;
    }

    private static string ActorOf(HttpContext ctx) =>
        ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? ctx.User.FindFirst("sub")?.Value
        ?? "unknown";
}

/// <summary>Logger category for admin audit events. No PII in messages (ids only).</summary>
public sealed class AdminAudit;
