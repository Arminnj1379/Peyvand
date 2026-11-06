using Messenger.Domain.Support;
using Messenger.Infrastructure.Persistence.Mongo.Documents;

namespace Messenger.Infrastructure.Persistence.Mongo.Mapping;

/// <summary>Document ↔ aggregate translation for support tickets.</summary>
internal static class TicketMapper
{
    public static TicketDocument ToDocument(SupportTicket ticket) => new()
    {
        Id = ticket.Id,
        CustomerId = ticket.CustomerId,
        Subject = ticket.Subject,
        Description = ticket.Description,
        Status = ticket.Status,
        AssigneeId = ticket.AssigneeId,
        ChatId = ticket.ChatId,
        CreatedAt = ticket.CreatedAt,
        UpdatedAt = ticket.UpdatedAt,
        ClosedAt = ticket.ClosedAt
    };

    public static SupportTicket ToDomain(TicketDocument doc) => SupportTicket.Rehydrate(new TicketSnapshot(
        doc.Id,
        doc.CustomerId,
        doc.Subject,
        doc.Description,
        doc.Status,
        doc.AssigneeId,
        doc.ChatId,
        doc.CreatedAt,
        doc.UpdatedAt,
        doc.ClosedAt));
}
