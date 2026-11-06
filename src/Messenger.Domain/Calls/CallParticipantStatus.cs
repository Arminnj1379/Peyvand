namespace Messenger.Domain.Calls;

/// <summary>Per-participant call state. Joining implies acceptance.</summary>
public enum CallParticipantStatus
{
    Invited = 1,
    Accepted = 2,
    Rejected = 3
}
