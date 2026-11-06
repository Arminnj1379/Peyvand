namespace Messenger.Domain.Calls;

/// <summary>Full stored state of a <see cref="Call"/> for rehydration. Infrastructure use only.</summary>
public sealed record CallParticipantSnapshot(
    string UserId,
    CallParticipantStatus Status,
    bool IsJoined,
    DateTime InvitedAt,
    DateTime? JoinedAt);

/// <summary>Full stored state of a <see cref="Call"/> for rehydration. Infrastructure use only.</summary>
public sealed record CallSnapshot(
    string Id,
    string ChatId,
    string InitiatorId,
    CallMediaType MediaType,
    CallStatus Status,
    string RoomName,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? EndedAt,
    string? EndedBy,
    CallEndReason? EndReason,
    IReadOnlyList<CallParticipantSnapshot> Participants);
