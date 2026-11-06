namespace Messenger.Domain.Calls;

/// <summary>Call media mode. Audio calls never publish camera tracks.</summary>
public enum CallMediaType
{
    Audio = 1,
    Video = 2
}
