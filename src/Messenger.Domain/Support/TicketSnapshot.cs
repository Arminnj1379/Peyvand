namespace Messenger.Domain.Support;

/// <summary>Full stored state of a <see cref="SupportTicket"/> for rehydration. Infrastructure use only.</summary>
public sealed record TicketSnapshot(
    string Id,
    string CustomerId,
    string Subject,
    string Description,
    TicketStatus Status,
    string? AssigneeId,
    string? ChatId,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ClosedAt);
