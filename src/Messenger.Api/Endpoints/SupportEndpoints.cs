using MediatR;
using Messenger.Application.Support;

namespace Messenger.Api.Endpoints;

public sealed record OpenTicketRequest(string Subject, string? Description);

public sealed record AssignTicketRequest(string? AgentId);

public sealed record SetTicketStatusRequest(string Status);

/// <summary>
/// Customer support inbox. Any signed-in user may open tickets; agents triage,
/// own, and resolve them. Mutations are audit-logged (actor + ticket + action).
/// </summary>
public static class SupportEndpoints
{
    public static IEndpointRouteBuilder MapSupportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/support").WithTags("Support").RequireAuthorization();

        group.MapPost("/tickets", async (OpenTicketRequest req, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new OpenTicketCommand(req.Subject, req.Description), ct)
                .ConfigureAwait(false);
            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("OpenTicket")
        .WithSummary("Opens a support ticket for the caller.")
        .Produces<TicketDto>(201)
        .ProducesProblem(400);

        group.MapGet("/tickets", async (string? status, string? assigneeId, ISender sender,
            CancellationToken ct, bool mine = false, int limit = 0) =>
            ResultMapper.ToHttp(
                await sender.Send(new ListTicketsQuery(status, assigneeId, mine, limit == 0 ? 20 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("ListTickets")
        .WithSummary("Ticket queue (agents see all; customers see only their own).")
        .Produces<IReadOnlyList<TicketDto>>()
        .ProducesProblem(400);

        group.MapGet("/tickets/{id}", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetTicketQuery(id), ct).ConfigureAwait(false)))
        .WithName("GetTicket")
        .Produces<TicketDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/tickets/{id}/assign", async (string id, AssignTicketRequest req,
            ISender sender, HttpContext ctx, ILogger<SupportAudit> audit, CancellationToken ct) =>
        {
            var result = await sender.Send(new AssignTicketCommand(id, req.AgentId), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                audit.LogInformation("Ticket {TicketId} assigned to {AssigneeId} by {Actor}. Chat {ChatId}.",
                    id, result.Value!.AssigneeId, ActorOf(ctx), result.Value.ChatId);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("AssignTicket")
        .WithSummary("Assigns a ticket (self when agent omitted); spins up the support conversation on first assignment.")
        .Produces<TicketDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/tickets/{id}/status", async (string id, SetTicketStatusRequest req,
            ISender sender, HttpContext ctx, ILogger<SupportAudit> audit, CancellationToken ct) =>
        {
            var result = await sender.Send(new SetTicketStatusCommand(id, req.Status), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                audit.LogInformation("Ticket {TicketId} moved to {Status} by {Actor}.",
                    id, result.Value!.Status, ActorOf(ctx));
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("SetTicketStatus")
        .WithSummary("Advances ticket workflow (agents move work forward; anyone involved may close).")
        .Produces<TicketDto>()
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

/// <summary>Logger category for support audit events. No PII in messages (ids only).</summary>
public sealed class SupportAudit;
