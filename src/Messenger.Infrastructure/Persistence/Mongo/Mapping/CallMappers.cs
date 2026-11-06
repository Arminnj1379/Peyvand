using Messenger.Domain.Calls;
using Messenger.Infrastructure.Persistence.Mongo.Documents;

namespace Messenger.Infrastructure.Persistence.Mongo.Mapping;

/// <summary>Document ↔ aggregate translation for calls.</summary>
internal static class CallMapper
{
    public static CallDocument ToDocument(Call call) => new()
    {
        Id = call.Id,
        ChatId = call.ChatId,
        InitiatorId = call.InitiatorId,
        MediaType = call.MediaType,
        Status = call.Status,
        RoomName = call.RoomName,
        Participants = call.Participants.Select(p => new CallParticipantDocument
        {
            UserId = p.UserId,
            Status = p.Status,
            IsJoined = p.IsJoined,
            InvitedAt = p.InvitedAt,
            JoinedAt = p.JoinedAt
        }).ToList(),
        CreatedAt = call.CreatedAt,
        StartedAt = call.StartedAt,
        EndedAt = call.EndedAt,
        EndedBy = call.EndedBy,
        EndReason = call.EndReason
    };

    public static Call ToDomain(CallDocument doc) => Call.Rehydrate(new CallSnapshot(
        doc.Id,
        doc.ChatId,
        doc.InitiatorId,
        doc.MediaType,
        doc.Status,
        doc.RoomName,
        doc.CreatedAt,
        doc.StartedAt,
        doc.EndedAt,
        doc.EndedBy,
        doc.EndReason,
        doc.Participants.Select(p => new CallParticipantSnapshot(
            p.UserId, p.Status, p.IsJoined, p.InvitedAt, p.JoinedAt)).ToList()));
}
