using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Services;
using Messenger.Domain.Common;
using Messenger.Domain.Support;
using Messenger.Domain.Users;

namespace Messenger.Application.Support;

public sealed record TicketDto(
    string Id,
    string CustomerId,
    string? CustomerName,
    string Subject,
    string Description,
    string Status,
    string? AssigneeId,
    string? AssigneeName,
    string? ChatId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt);

/// <summary>Who may act as an agent: support role or any administrator.</summary>
internal static class SupportAccess
{
    public static bool IsAgent(CallerContext caller) =>
        caller.HasRole(nameof(Role.SupportAgent)) ||
        caller.HasRole(nameof(Role.OrganizationAdministrator)) ||
        caller.HasRole(nameof(Role.SystemAdministrator));

    public static async Task<TicketDto> ToDtoAsync(
        ISupportTicketStore _,
        IUserStore users,
        SupportTicket ticket,
        CancellationToken ct)
    {
        var map = await NameMapAsync(users,
            ticket.AssigneeId is null ? [ticket.CustomerId] : [ticket.CustomerId, ticket.AssigneeId], ct)
            .ConfigureAwait(false);
        return ToDto(ticket, map);
    }

    /// <summary>
    /// Batched projection for ticket pages: one user round-trip for every
    /// customer + assignee on the page (was: up to two lookups per ticket).
    /// </summary>
    public static async Task<IReadOnlyList<TicketDto>> ToDtoManyAsync(
        IUserStore users,
        IReadOnlyList<SupportTicket> found,
        CancellationToken ct)
    {
        var ids = new List<string>();
        foreach (var t in found)
        {
            ids.Add(t.CustomerId);
            if (t.AssigneeId is not null)
            {
                ids.Add(t.AssigneeId);
            }
        }

        var map = await NameMapAsync(users, ids, ct).ConfigureAwait(false);
        return found.Select(t => ToDto(t, map)).ToList();
    }

    private static async Task<Dictionary<string, string?>> NameMapAsync(
        IUserStore users, IEnumerable<string> ids, CancellationToken ct)
    {
        var map = new Dictionary<string, string?>();
        foreach (var user in await users.ListByIdsAsync(ids.Distinct().ToList(), ct).ConfigureAwait(false))
        {
            map[user.Id] = user.DisplayName;
        }

        return map;
    }

    private static TicketDto ToDto(SupportTicket ticket, Dictionary<string, string?> names)
    {
        names.TryGetValue(ticket.CustomerId, out var customerName);
        string? assigneeName = null;
        if (ticket.AssigneeId is not null)
        {
            names.TryGetValue(ticket.AssigneeId, out assigneeName);
        }

        return new TicketDto(
            ticket.Id, ticket.CustomerId, customerName, ticket.Subject, ticket.Description,
            ticket.Status.ToString(), ticket.AssigneeId, assigneeName, ticket.ChatId,
            ticket.CreatedAt, ticket.UpdatedAt, ticket.ClosedAt);
    }

    public static Result<SupportTicket> RequireVisible(SupportTicket? ticket, string callerId, bool agent)
    {
        if (ticket is null)
        {
            return Result<SupportTicket>.Fail(SupportErrors.NotFound);
        }

        if (!agent && ticket.CustomerId != callerId)
        {
            return Result<SupportTicket>.Fail(SupportErrors.Forbidden);
        }

        return Result<SupportTicket>.Ok(ticket);
    }
}

/// <summary>Any signed-in user may open a ticket (customers and employees alike).</summary>
public sealed record OpenTicketCommand(string Subject, string? Description);

public sealed class OpenTicketValidator : AbstractValidator<OpenTicketCommand>
{
    public OpenTicketValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MinimumLength(1).MaximumLength(SupportTicket.MaxSubjectLength);
        RuleFor(x => x.Description).MaximumLength(SupportTicket.MaxDescriptionLength);
    }
}

/// <summary>
/// Agents see the queue (all tickets + filters); customers see only their own.
/// </summary>
public sealed record ListTicketsQuery(string? Status, string? AssigneeId, bool Mine, int Limit);

public sealed class ListTicketsValidator : AbstractValidator<ListTicketsQuery>
{
    public ListTicketsValidator()
    {
        RuleFor(x => x.Status).Must(s => s is null ||
            Enum.TryParse<TicketStatus>(s, ignoreCase: true, out _))
            .WithMessage("Unknown ticket status.");
        RuleFor(x => x.AssigneeId).MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed record GetTicketQuery(string TicketId);

public sealed class GetTicketValidator : AbstractValidator<GetTicketQuery>
{
    public GetTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty().MaximumLength(64);
    }
}

/// <summary>
/// Agent takes ownership (self when AgentId omitted). First assignment spins
/// up the support conversation (customer + agent group chat) and links it.
/// </summary>
public sealed record AssignTicketCommand(string TicketId, string? AgentId);
public sealed class AssignTicketValidator : AbstractValidator<AssignTicketCommand>
{
    public AssignTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.AgentId).MaximumLength(64);
    }
}

public sealed record SetTicketStatusCommand(string TicketId, string Status);

public sealed class SetTicketStatusValidator : AbstractValidator<SetTicketStatusCommand>
{
    public SetTicketStatusValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Status).NotEmpty().Must(s =>
            Enum.TryParse<TicketStatus>(s, ignoreCase: true, out _))
            .WithMessage("Unknown ticket status.");
    }
}
