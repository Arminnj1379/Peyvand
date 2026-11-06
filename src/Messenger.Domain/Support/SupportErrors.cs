using Messenger.Domain.Common;

namespace Messenger.Domain.Support;

/// <summary>Machine-readable support failures. Messages are safe to surface.</summary>
public static class SupportErrors
{
    public static readonly Error NotFound =
        new("support.not_found", "Support ticket not found.");

    public static readonly Error Forbidden =
        new("support.forbidden", "You cannot access this support ticket.");

    public static readonly Error AgentOnly =
        new("support.agent_only", "Only support agents can perform this action.");

    public static readonly Error InvalidStatus =
        new("support.invalid_status", "This status change is not allowed.");

    public static readonly Error AlreadyClosed =
        new("support.already_closed", "This ticket is already closed.");

    public static readonly Error AssigneeRequired =
        new("support.assignee_required", "A ticket must be assigned before work starts.");

    public static readonly Error InvalidSubject =
        new("support.invalid_subject", "Subject must be 1-128 characters.");

    public static readonly Error InvalidDescription =
        new("support.invalid_description", "Description must be 2000 characters or fewer.");
}
