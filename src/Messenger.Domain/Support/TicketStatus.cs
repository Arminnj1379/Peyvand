namespace Messenger.Domain.Support;

/// <summary>Support ticket lifecycle. Closed is terminal; Resolved can reopen.</summary>
public enum TicketStatus
{
    Open = 1,
    Assigned = 2,
    InProgress = 3,
    Resolved = 4,
    Closed = 5
}
