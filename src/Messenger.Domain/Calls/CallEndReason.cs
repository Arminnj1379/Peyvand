namespace Messenger.Domain.Calls;

/// <summary>Why a call ended. Drives history labels (completed/declined/cancelled).</summary>
public enum CallEndReason
{
    Completed = 1,
    Declined = 2,
    Cancelled = 3
}
