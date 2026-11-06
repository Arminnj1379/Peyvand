using Messenger.Application.Support;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Customer support inbox: open/triage/own/resolve tickets.</summary>
public interface ISupportService
{
    Task<Result<TicketDto>> OpenTicketAsync(
        OpenTicketCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<TicketDto>>> ListTicketsAsync(
        ListTicketsQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<TicketDto>> GetTicketAsync(
        GetTicketQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<TicketDto>> AssignTicketAsync(
        AssignTicketCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<TicketDto>> SetTicketStatusAsync(
        SetTicketStatusCommand command, CallerContext caller, CancellationToken ct);
}
