using Messenger.Domain.Common;

namespace Messenger.Domain.Calls;

/// <summary>Machine-readable call failures. Messages are safe to surface.</summary>
public static class CallErrors
{
    public static readonly Error NotFound =
        new("call.not_found", "Call not found.");

    public static readonly Error Forbidden =
        new("call.forbidden", "You are not a participant of this conversation.");

    public static readonly Error CannotStart =
        new("call.cannot_start", "You cannot start a call in this conversation.");

    public static readonly Error AlreadyActive =
        new("call.already_active", "This conversation already has an active call.");

    public static readonly Error Ended =
        new("call.ended", "This call has already ended.");

    public static readonly Error NotInitiator =
        new("call.not_initiator", "Only the caller or an admin can end this call.");

    public static readonly Error InvalidMediaType =
        new("call.invalid_media", "Call type must be audio or video.");

    public static readonly Error LiveKitNotConfigured =
        new("call.livekit_not_configured", "Voice and video calls are temporarily unavailable.");
}
