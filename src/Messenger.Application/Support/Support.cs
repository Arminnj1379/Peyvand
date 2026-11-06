using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Domain.Chats;
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
    public static bool IsAgent(ICurrentUser caller) =>
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

public sealed record OpenTicketCommand(string Subject, string? Description)
    : IRequest<Result<TicketDto>>;

public sealed class OpenTicketValidator : AbstractValidator<OpenTicketCommand>
{
    public OpenTicketValidator()
    {
        RuleFor(x => x.Subject).NotEmpty().MinimumLength(1).MaximumLength(SupportTicket.MaxSubjectLength);
        RuleFor(x => x.Description).MaximumLength(SupportTicket.MaxDescriptionLength);
    }
}

/// <summary>Any signed-in user may open a ticket (customers and employees alike).</summary>
public sealed class OpenTicketHandler(
    ISupportTicketStore tickets,
    IUserStore users,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<OpenTicketCommand, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> Handle(OpenTicketCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        var opened = SupportTicket.Open(
            ids.NewId(), currentUser.UserId, request.Subject, request.Description, clock.UtcNow);
        if (opened.IsFailure)
        {
            return Result<TicketDto>.Fail(opened.Error);
        }

        await tickets.AddAsync(opened.Value!, ct).ConfigureAwait(false);
        return Result<TicketDto>.Ok(
            await SupportAccess.ToDtoAsync(tickets, users, opened.Value!, ct).ConfigureAwait(false));
    }
}

public sealed record ListTicketsQuery(string? Status, string? AssigneeId, bool Mine, int Limit)
    : IRequest<Result<IReadOnlyList<TicketDto>>>;

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

/// <summary>
/// Agents see the queue (all tickets + filters); customers see only their own.
/// </summary>
public sealed class ListTicketsHandler(
    ISupportTicketStore tickets,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<ListTicketsQuery, Result<IReadOnlyList<TicketDto>>>
{
    public async Task<Result<IReadOnlyList<TicketDto>>> Handle(ListTicketsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<TicketDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var agent = SupportAccess.IsAgent(currentUser);
        Enum.TryParse<TicketStatus>(request.Status, ignoreCase: true, out var status);
        var found = await tickets.ListAsync(
            request.Status is null ? null : status,
            !agent || request.Mine ? null : request.AssigneeId,
            agent && !request.Mine ? null : currentUser.UserId,
            request.Limit, ct).ConfigureAwait(false);

        // Customers asking for "mine" with an assignee filter still only see own.
        if (!agent)
        {
            found = found.Where(t => t.CustomerId == currentUser.UserId).ToList();
        }

        return Result<IReadOnlyList<TicketDto>>.Ok(
            await SupportAccess.ToDtoManyAsync(users, found, ct).ConfigureAwait(false));
    }
}

public sealed record GetTicketQuery(string TicketId) : IRequest<Result<TicketDto>>;

public sealed class GetTicketValidator : AbstractValidator<GetTicketQuery>
{
    public GetTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetTicketHandler(
    ISupportTicketStore tickets,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<GetTicketQuery, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> Handle(GetTicketQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        var ticket = await tickets.GetByIdAsync(request.TicketId, ct).ConfigureAwait(false);
        var visible = SupportAccess.RequireVisible(ticket, currentUser.UserId, SupportAccess.IsAgent(currentUser));
        if (visible.IsFailure)
        {
            return Result<TicketDto>.Fail(visible.Error);
        }

        return Result<TicketDto>.Ok(
            await SupportAccess.ToDtoAsync(tickets, users, visible.Value!, ct).ConfigureAwait(false));
    }
}

public sealed record AssignTicketCommand(string TicketId, string? AgentId)
    : IRequest<Result<TicketDto>>;

public sealed class AssignTicketValidator : AbstractValidator<AssignTicketCommand>
{
    public AssignTicketValidator()
    {
        RuleFor(x => x.TicketId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.AgentId).MaximumLength(64);
    }
}

/// <summary>
/// Agent takes ownership (self when AgentId omitted). First assignment spins
/// up the support conversation (customer + agent group chat) and links it.
/// </summary>
public sealed class AssignTicketHandler(
    ISupportTicketStore tickets,
    IChatStore chats,
    IUserStore users,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<AssignTicketCommand, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> Handle(AssignTicketCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        if (!SupportAccess.IsAgent(currentUser))
        {
            return Result<TicketDto>.Fail(SupportErrors.AgentOnly);
        }

        var ticket = await tickets.GetByIdAsync(request.TicketId, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result<TicketDto>.Fail(SupportErrors.NotFound);
        }

        var agentId = string.IsNullOrWhiteSpace(request.AgentId) ? currentUser.UserId : request.AgentId.Trim();
        var agent = await users.GetByIdAsync(agentId, ct).ConfigureAwait(false);
        if (agent is null || !agent.IsActive || !CanOwnTicket(agent))
        {
            return Result<TicketDto>.Fail(SupportErrors.AgentOnly);
        }

        var now = clock.UtcNow;
        var assigned = ticket.Assign(agentId, now);
        if (assigned.IsFailure)
        {
            return Result<TicketDto>.Fail(assigned.Error);
        }

        if (ticket.ChatId is null)
        {
            var customer = await users.GetByIdAsync(ticket.CustomerId, ct).ConfigureAwait(false);
            if (customer is null || !customer.IsActive)
            {
                return Result<TicketDto>.Fail(SupportErrors.NotFound);
            }

            var title = $"Support: {ticket.Subject}";
            if (title.Length > 64)
            {
                title = title[..64];
            }

            var chat = Chat.CreateGroup(
                ids.NewId(), agentId, title, [ticket.CustomerId, agentId],
                $"Ticket {ticket.Id}", now);
            if (chat.IsFailure)
            {
                return Result<TicketDto>.Fail(chat.Error);
            }

            await chats.AddAsync(chat.Value!, ct).ConfigureAwait(false);
            var linked = ticket.LinkChat(chat.Value!.Id, now);
            if (linked.IsFailure)
            {
                return Result<TicketDto>.Fail(linked.Error);
            }
        }

        await tickets.UpdateAsync(ticket, ct).ConfigureAwait(false);
        return Result<TicketDto>.Ok(
            await SupportAccess.ToDtoAsync(tickets, users, ticket, ct).ConfigureAwait(false));
    }

    private static bool CanOwnTicket(Domain.Users.User agent) =>
        agent.HasRole(Role.SupportAgent) ||
        agent.HasRole(Role.OrganizationAdministrator) ||
        agent.HasRole(Role.SystemAdministrator);
}

public sealed record SetTicketStatusCommand(string TicketId, string Status)
    : IRequest<Result<TicketDto>>;

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

public sealed class SetTicketStatusHandler(
    ISupportTicketStore tickets,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<SetTicketStatusCommand, Result<TicketDto>>
{
    public async Task<Result<TicketDto>> Handle(SetTicketStatusCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        var ticket = await tickets.GetByIdAsync(request.TicketId, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result<TicketDto>.Fail(SupportErrors.NotFound);
        }

        var agent = SupportAccess.IsAgent(currentUser);
        if (!agent && ticket.CustomerId != currentUser.UserId)
        {
            return Result<TicketDto>.Fail(SupportErrors.Forbidden);
        }

        Enum.TryParse<TicketStatus>(request.Status, ignoreCase: true, out var status);
        var changed = ticket.SetStatus(status, agent, clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result<TicketDto>.Fail(changed.Error);
        }

        await tickets.UpdateAsync(ticket, ct).ConfigureAwait(false);
        return Result<TicketDto>.Ok(
            await SupportAccess.ToDtoAsync(tickets, users, ticket, ct).ConfigureAwait(false));
    }
}
