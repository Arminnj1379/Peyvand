using Messenger.Domain.Support;

namespace Messenger.Application.Abstractions;

/// <summary>Support ticket store (collection <c>tickets</c>).</summary>
public interface ISupportTicketStore
{
    Task<SupportTicket?> GetByIdAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<SupportTicket>> ListAsync(
        TicketStatus? status, string? assigneeId, string? customerId, int limit, CancellationToken ct);
    Task AddAsync(SupportTicket ticket, CancellationToken ct);
    Task UpdateAsync(SupportTicket ticket, CancellationToken ct);
}
