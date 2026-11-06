namespace Messenger.Domain.Calls;

/// <summary>
/// Call lifecycle. Ringing → Active on first media join; Active → Ended on
/// end (or when the last participant leaves). Ended is terminal.
/// </summary>
public enum CallStatus
{
    Ringing = 1,
    Active = 2,
    Ended = 3
}
