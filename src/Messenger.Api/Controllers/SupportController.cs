using Messenger.Api.Extensions;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Support;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

public sealed record OpenTicketRequest(string Subject, string? Description);

public sealed record AssignTicketRequest(string? AgentId);

public sealed record SetTicketStatusRequest(string Status);

/// <summary>
/// Customer support inbox. Any signed-in user may open tickets; agents triage,
/// own, and resolve them. Mutations are audit-logged (actor + ticket + action).
/// </summary>
[ApiController]
[Route("api/v1/support")]
[Authorize]
public sealed class SupportController(ISupportService support, ILogger<SupportAudit> audit) : ControllerBase
{
    [HttpPost("tickets")]
    [ProducesResponseType(typeof(TicketDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> OpenTicket([FromBody] OpenTicketRequest req, CancellationToken ct) =>
        ResultMapper.ToHttp(await support.OpenTicketAsync(
            new OpenTicketCommand(req.Subject, req.Description), User.ToCaller(), ct).ConfigureAwait(false), 201);

    [HttpGet("tickets")]
    [ProducesResponseType(typeof(IReadOnlyList<TicketDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> ListTickets(
        [FromQuery] string? status,
        [FromQuery] string? assigneeId,
        [FromQuery] bool mine = false,
        [FromQuery] int limit = 0,
        CancellationToken ct = default) =>
        ResultMapper.ToHttp(await support.ListTicketsAsync(new ListTicketsQuery(
            status, assigneeId, mine, limit == 0 ? 20 : limit), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("tickets/{id}")]
    [ProducesResponseType(typeof(TicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetTicket([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await support.GetTicketAsync(new GetTicketQuery(id), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpPost("tickets/{id}/assign")]
    [ProducesResponseType(typeof(TicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> AssignTicket(
        [FromRoute] string id, [FromBody] AssignTicketRequest req, CancellationToken ct)
    {
        var result = await support.AssignTicketAsync(
            new AssignTicketCommand(id, req.AgentId), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            audit.LogInformation("Ticket {TicketId} assigned to {AssigneeId} by {Actor}. Chat {ChatId}.",
                id, result.Value!.AssigneeId, User.ActorId(), result.Value.ChatId);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("tickets/{id}/status")]
    [ProducesResponseType(typeof(TicketDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> SetTicketStatus(
        [FromRoute] string id, [FromBody] SetTicketStatusRequest req, CancellationToken ct)
    {
        var result = await support.SetTicketStatusAsync(
            new SetTicketStatusCommand(id, req.Status), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            audit.LogInformation("Ticket {TicketId} moved to {Status} by {Actor}.",
                id, result.Value!.Status, User.ActorId());
        }

        return ResultMapper.ToHttp(result);
    }
}

/// <summary>Logger category for support audit events. No PII in messages (ids only).</summary>
public sealed class SupportAudit;
