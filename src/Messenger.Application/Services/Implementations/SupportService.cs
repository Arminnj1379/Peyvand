using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Support;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Support;
using Messenger.Domain.Users;

namespace Messenger.Application.Services.Implementations;

/// <summary>Support inbox use cases. Logic moved verbatim from the former MediatR handlers.</summary>
public sealed class SupportService(
    ISupportTicketStore tickets,
    IChatStore chats,
    IUserStore users,
    IIdGenerator ids,
    IClock clock) : ISupportService
{
    public async Task<Result<TicketDto>> OpenTicketAsync(
        OpenTicketCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new OpenTicketValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        var opened = SupportTicket.Open(
            ids.NewId(), caller.UserId, command.Subject, command.Description, clock.UtcNow);
        if (opened.IsFailure)
        {
            return Result<TicketDto>.Fail(opened.Error);
        }

        await tickets.AddAsync(opened.Value!, ct).ConfigureAwait(false);
        return Result<TicketDto>.Ok(
            await SupportAccess.ToDtoAsync(tickets, users, opened.Value!, ct).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<TicketDto>>> ListTicketsAsync(
        ListTicketsQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new ListTicketsValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<TicketDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var agent = SupportAccess.IsAgent(caller);
        Enum.TryParse<TicketStatus>(query.Status, ignoreCase: true, out var status);
        var found = await tickets.ListAsync(
            query.Status is null ? null : status,
            !agent || query.Mine ? null : query.AssigneeId,
            agent && !query.Mine ? null : caller.UserId,
            query.Limit, ct).ConfigureAwait(false);

        // Customers asking for "mine" with an assignee filter still only see own.
        if (!agent)
        {
            found = found.Where(t => t.CustomerId == caller.UserId).ToList();
        }

        return Result<IReadOnlyList<TicketDto>>.Ok(
            await SupportAccess.ToDtoManyAsync(users, found, ct).ConfigureAwait(false));
    }

    public async Task<Result<TicketDto>> GetTicketAsync(
        GetTicketQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetTicketValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        var ticket = await tickets.GetByIdAsync(query.TicketId, ct).ConfigureAwait(false);
        var visible = SupportAccess.RequireVisible(ticket, caller.UserId, SupportAccess.IsAgent(caller));
        if (visible.IsFailure)
        {
            return Result<TicketDto>.Fail(visible.Error);
        }

        return Result<TicketDto>.Ok(
            await SupportAccess.ToDtoAsync(tickets, users, visible.Value!, ct).ConfigureAwait(false));
    }

    public async Task<Result<TicketDto>> AssignTicketAsync(
        AssignTicketCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new AssignTicketValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        if (!SupportAccess.IsAgent(caller))
        {
            return Result<TicketDto>.Fail(SupportErrors.AgentOnly);
        }

        var ticket = await tickets.GetByIdAsync(command.TicketId, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result<TicketDto>.Fail(SupportErrors.NotFound);
        }

        var agentId = string.IsNullOrWhiteSpace(command.AgentId) ? caller.UserId : command.AgentId.Trim();
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

    public async Task<Result<TicketDto>> SetTicketStatusAsync(
        SetTicketStatusCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SetTicketStatusValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<TicketDto>.Fail(AuthErrors.Unauthenticated);
        }

        var ticket = await tickets.GetByIdAsync(command.TicketId, ct).ConfigureAwait(false);
        if (ticket is null)
        {
            return Result<TicketDto>.Fail(SupportErrors.NotFound);
        }

        var agent = SupportAccess.IsAgent(caller);
        if (!agent && ticket.CustomerId != caller.UserId)
        {
            return Result<TicketDto>.Fail(SupportErrors.Forbidden);
        }

        Enum.TryParse<TicketStatus>(command.Status, ignoreCase: true, out var status);
        var changed = ticket.SetStatus(status, agent, clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result<TicketDto>.Fail(changed.Error);
        }

        await tickets.UpdateAsync(ticket, ct).ConfigureAwait(false);
        return Result<TicketDto>.Ok(
            await SupportAccess.ToDtoAsync(tickets, users, ticket, ct).ConfigureAwait(false));
    }

    private static bool CanOwnTicket(User agent) =>
        agent.HasRole(Role.SupportAgent) ||
        agent.HasRole(Role.OrganizationAdministrator) ||
        agent.HasRole(Role.SystemAdministrator);
}
