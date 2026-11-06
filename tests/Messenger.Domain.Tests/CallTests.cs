using Messenger.Domain.Calls;

namespace Messenger.Domain.Tests;

public sealed class CallTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static Call Start(string id = "c1") =>
        Call.Create(id, "chat1", "u1", CallMediaType.Video, ["u1", "u2", "u3"], Now).Value!;

    [Fact]
    public void Create_rings_members_with_initiator_accepted()
    {
        var call = Start();

        Assert.Equal(CallStatus.Ringing, call.Status);
        Assert.Equal("call-c1", call.RoomName);
        Assert.True(call.IsLive);
        Assert.Equal(3, call.Participants.Count);
        Assert.Equal(CallParticipantStatus.Accepted, call.GetParticipant("u1")!.Status);
        Assert.Equal(CallParticipantStatus.Invited, call.GetParticipant("u2")!.Status);
    }

    [Fact]
    public void Create_rejects_initiator_outside_members()
    {
        var result = Call.Create("c1", "chat1", "stranger", CallMediaType.Audio, ["u1", "u2"], Now);

        Assert.True(result.IsFailure);
        Assert.Equal(CallErrors.Forbidden, result.Error);
    }

    [Fact]
    public void First_join_activates_call_and_implies_accept()
    {
        var call = Start();

        Assert.True(call.MarkJoined("u2", Now).IsSuccess);
        Assert.Equal(CallStatus.Active, call.Status);
        Assert.Equal(Now, call.StartedAt);
        Assert.Equal(CallParticipantStatus.Accepted, call.GetParticipant("u2")!.Status);
        Assert.True(call.GetParticipant("u2")!.IsJoined);
    }

    [Fact]
    public void Reject_then_join_reaccepts()
    {
        var call = Start();

        Assert.True(call.Reject("u2").IsSuccess);
        Assert.Equal(CallParticipantStatus.Rejected, call.GetParticipant("u2")!.Status);

        Assert.True(call.MarkJoined("u2", Now).IsSuccess);
        Assert.Equal(CallParticipantStatus.Accepted, call.GetParticipant("u2")!.Status);
    }

    [Fact]
    public void Last_leave_empties_room()
    {
        var call = Start();
        call.MarkJoined("u1", Now);
        call.MarkJoined("u2", Now);

        Assert.False(call.MarkLeft("u1"));
        Assert.True(call.MarkLeft("u2"));
        Assert.True(call.IsLive); // domain reports; the handler ends it.
    }

    [Fact]
    public void End_is_terminal()
    {
        var call = Start();
        call.MarkJoined("u1", Now);

        Assert.True(call.End("u1", CallEndReason.Completed, Now).IsSuccess);
        Assert.Equal(CallStatus.Ended, call.Status);
        Assert.False(call.IsLive);

        Assert.True(call.Accept("u2", Now).IsFailure);
        Assert.True(call.MarkJoined("u2", Now).IsFailure);
        Assert.True(call.End("u1", CallEndReason.Completed, Now).IsFailure);
    }

    [Fact]
    public void Duration_counts_from_first_join()
    {
        var call = Start();

        Assert.Equal(0, call.DurationSeconds);
        call.MarkJoined("u1", Now);
        call.End("u1", CallEndReason.Completed, Now.AddMinutes(3));
        Assert.Equal(180, call.DurationSeconds);
    }

    [Fact]
    public void Rehydrate_round_trips_state()
    {
        var call = Start();
        call.MarkJoined("u2", Now);
        call.End("u1", CallEndReason.Completed, Now.AddMinutes(1));

        var revived = Call.Rehydrate(new CallSnapshot(
            call.Id, call.ChatId, call.InitiatorId, call.MediaType, call.Status,
            call.RoomName, call.CreatedAt, call.StartedAt, call.EndedAt,
            call.EndedBy, call.EndReason,
            call.Participants.Select(p => new CallParticipantSnapshot(
                p.UserId, p.Status, p.IsJoined, p.InvitedAt, p.JoinedAt)).ToList()));

        Assert.Equal(CallStatus.Ended, revived.Status);
        Assert.Equal("u1", revived.EndedBy);
        Assert.Equal(2, revived.Participants.Count(p => p.Status == CallParticipantStatus.Accepted));
        Assert.Empty(revived.DomainEvents);
    }
}
