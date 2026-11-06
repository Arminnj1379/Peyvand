using Messenger.Domain.Common;

namespace Messenger.Domain.Support;

/// <summary>
/// Customer support ticket. Customers open and track their own tickets;
/// agents triage, own, and resolve them. The linked chat (created on first
/// assignment) carries the actual conversation — the ticket tracks workflow.
/// </summary>
public sealed class SupportTicket : Entity
{
    public const int MaxSubjectLength = 128;
    public const int MaxDescriptionLength = 2000;

    private SupportTicket(
        string id,
        string customerId,
        string subject,
        string description,
        DateTime now) : base(id)
    {
        CustomerId = customerId;
        Subject = subject;
        Description = description;
        Status = TicketStatus.Open;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public string CustomerId { get; }
    public string Subject { get; }
    public string Description { get; }
    public TicketStatus Status { get; private set; }
    public string? AssigneeId { get; private set; }
    public string? ChatId { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }

    public bool IsClosed => Status == TicketStatus.Closed;

    public static Result<SupportTicket> Open(
        string id,
        string customerId,
        string subject,
        string? description,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        if (string.IsNullOrWhiteSpace(subject) || subject.Trim().Length > MaxSubjectLength)
        {
            return Result<SupportTicket>.Fail(SupportErrors.InvalidSubject);
        }

        if (description is not null && description.Length > MaxDescriptionLength)
        {
            return Result<SupportTicket>.Fail(SupportErrors.InvalidDescription);
        }

        return Result<SupportTicket>.Ok(new SupportTicket(
            id, customerId, subject.Trim(), description?.Trim() ?? string.Empty, now));
    }

    /// <summary>Agent takes ownership. First assignment moves Open→Assigned.</summary>
    public Result Assign(string agentId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (IsClosed)
        {
            return Result.Fail(SupportErrors.AlreadyClosed);
        }

        if (agentId == CustomerId)
        {
            return Result.Fail(SupportErrors.AgentOnly);
        }

        AssigneeId = agentId;
        if (Status == TicketStatus.Open)
        {
            Status = TicketStatus.Assigned;
        }

        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>Links the support conversation (created once, on assignment).</summary>
    public Result LinkChat(string chatId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        if (IsClosed)
        {
            return Result.Fail(SupportErrors.AlreadyClosed);
        }

        ChatId = chatId;
        UpdatedAt = now;
        return Result.Ok();
    }

    /// <summary>
    /// Workflow transition. Agents move work forward; anyone involved may close.
    /// Resolved can reopen to InProgress (regression reports).
    /// </summary>
    public Result SetStatus(TicketStatus status, bool byAgent, DateTime now)
    {
        if (!Enum.IsDefined(status))
        {
            return Result.Fail(SupportErrors.InvalidStatus);
        }

        if (IsClosed)
        {
            return Result.Fail(SupportErrors.AlreadyClosed);
        }

        var allowed = (Status, status, byAgent) switch
        {
            // Closing is always allowed (cancel by customer, resolve-close by agent).
            (_, TicketStatus.Closed, _) => true,
            // Work states require an agent and an owner.
            (TicketStatus.Open, TicketStatus.Assigned, true) => true,
            (TicketStatus.Assigned, TicketStatus.InProgress, true) => true,
            (TicketStatus.InProgress, TicketStatus.Resolved, true) => true,
            (TicketStatus.Resolved, TicketStatus.InProgress, true) => true,
            _ => false
        };

        if (!allowed)
        {
            return Result.Fail(SupportErrors.InvalidStatus);
        }

        if (status is TicketStatus.Assigned or TicketStatus.InProgress or TicketStatus.Resolved &&
            AssigneeId is null)
        {
            return Result.Fail(SupportErrors.AssigneeRequired);
        }

        Status = status;
        UpdatedAt = now;
        if (status == TicketStatus.Closed)
        {
            ClosedAt = now;
        }

        return Result.Ok();
    }

    /// <summary>
    /// Persistence rehydration. Bypasses <see cref="Open"/> (no re-validation
    /// of already-stored state). Infrastructure use only.
    /// </summary>
    public static SupportTicket Rehydrate(TicketSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var ticket = new SupportTicket(
            snapshot.Id, snapshot.CustomerId, snapshot.Subject,
            snapshot.Description, snapshot.CreatedAt);
        ticket.Status = snapshot.Status;
        ticket.AssigneeId = snapshot.AssigneeId;
        ticket.ChatId = snapshot.ChatId;
        ticket.UpdatedAt = snapshot.UpdatedAt;
        ticket.ClosedAt = snapshot.ClosedAt;
        return ticket;
    }
}
