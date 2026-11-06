using Messenger.Domain.Calls;

namespace Messenger.Application.Calls;

/// <summary>Call projection. Participant names resolve client-side from chat members.</summary>
public sealed record CallParticipantDto(
    string UserId,
    string Status,
    bool IsJoined,
    DateTime? JoinedAt);

public sealed record CallDto(
    string Id,
    string ChatId,
    string InitiatorId,
    string MediaType,
    string Status,
    string RoomName,
    IReadOnlyList<CallParticipantDto> Participants,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? EndedAt,
    string? EndedBy,
    string? EndReason,
    long DurationSeconds);

/// <summary>Call plus the one-shot media credentials for the joiner.</summary>
public sealed record CallJoinDto(
    CallDto Call,
    string Token,
    string Url,
    string RoomName,
    string Identity);

internal static class CallMapping
{
    public static CallDto ToDto(Call call) =>
        new(call.Id, call.ChatId, call.InitiatorId, call.MediaType.ToString(),
            call.Status.ToString(), call.RoomName,
            call.Participants
                .Select(p => new CallParticipantDto(
                    p.UserId, p.Status.ToString(), p.IsJoined, p.JoinedAt))
                .ToList(),
            call.CreatedAt, call.StartedAt, call.EndedAt, call.EndedBy,
            call.EndReason?.ToString(), call.DurationSeconds);
}
