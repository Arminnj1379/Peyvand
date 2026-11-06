using Messenger.Application.Abstractions;
using Messenger.Domain.Support;

namespace Messenger.Application.Tests;

/// <summary>In-memory ticket port for handler tests. No Mongo.</summary>
internal sealed class FakeTicketStore : ISupportTicketStore
{
    private readonly Dictionary<string, SupportTicket> _tickets = [];

    public Task<SupportTicket?> GetByIdAsync(string id, CancellationToken ct) =>
        Task.FromResult(_tickets.TryGetValue(id, out var t) ? t : null);

    public Task<IReadOnlyList<SupportTicket>> ListAsync(
        TicketStatus? status, string? assigneeId, string? customerId, int limit, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<SupportTicket>>(_tickets.Values
            .Where(t => (!status.HasValue || t.Status == status.Value) &&
                (assigneeId is null || t.AssigneeId == assigneeId) &&
                (customerId is null || t.CustomerId == customerId))
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToList());

    public Task AddAsync(SupportTicket ticket, CancellationToken ct)
    {
        _tickets[ticket.Id] = ticket;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SupportTicket ticket, CancellationToken ct)
    {
        _tickets[ticket.Id] = ticket;
        return Task.CompletedTask;
    }
}
