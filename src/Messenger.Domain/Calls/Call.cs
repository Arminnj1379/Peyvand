using Messenger.Domain.Common;

namespace Messenger.Domain.Calls;

/// <summary>One invited participant. Joining implies acceptance.</summary>
public sealed class CallParticipant
{
    internal CallParticipant(string userId, CallParticipantStatus status, DateTime invitedAt)
    {
        UserId = userId;
        Status = status;
        InvitedAt = invitedAt;
    }

    public string UserId { get; }
    public CallParticipantStatus Status { get; internal set; }
    public bool IsJoined { get; internal set; }
    public DateTime InvitedAt { get; }
    public DateTime? JoinedAt { get; internal set; }
}

/// <summary>
/// Voice/video call aggregate. Signaling state lives here (MongoDB collection
/// <c>calls</c>); actual media flows peer-to-SFU through LiveKit and never
/// touches the API. One live call per chat (enforced by handlers).
/// </summary>
public sealed class Call : Entity
{
    private readonly List<CallParticipant> _participants = [];

    private Call(
        string id,
        string chatId,
        string initiatorId,
        CallMediaType mediaType,
        string roomName,
        DateTime now) : base(id)
    {
        ChatId = chatId;
        InitiatorId = initiatorId;
        MediaType = mediaType;
        RoomName = roomName;
        Status = CallStatus.Ringing;
        CreatedAt = now;
    }

    public string ChatId { get; }
    public string InitiatorId { get; }
    public CallMediaType MediaType { get; }
    public CallStatus Status { get; private set; }
    public string RoomName { get; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? EndedAt { get; private set; }
    public string? EndedBy { get; private set; }
    public CallEndReason? EndReason { get; private set; }
    public IReadOnlyList<CallParticipant> Participants => _participants.AsReadOnly();

    public bool IsLive => Status != CallStatus.Ended;

    public long DurationSeconds =>
        StartedAt is null ? 0 : (long)((EndedAt ?? DateTime.UtcNow) - StartedAt.Value).TotalSeconds;

    public CallParticipant? GetParticipant(string userId) =>
        _participants.FirstOrDefault(p => p.UserId == userId);

    /// <summary>
    /// Starts a call for every current chat member. The initiator is
    /// pre-accepted (they join immediately); everyone else rings.
    /// </summary>
    public static Result<Call> Create(
        string id,
        string chatId,
        string initiatorId,
        CallMediaType mediaType,
        IEnumerable<string> memberIds,
        DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(chatId);
        ArgumentException.ThrowIfNullOrWhiteSpace(initiatorId);
        if (!Enum.IsDefined(mediaType))
        {
            return Result<Call>.Fail(CallErrors.InvalidMediaType);
        }

        var members = memberIds.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().ToList();
        if (!members.Contains(initiatorId))
        {
            return Result<Call>.Fail(CallErrors.Forbidden);
        }

        var call = new Call(id, chatId, initiatorId, mediaType, $"call-{id}", now);
        foreach (var memberId in members)
        {
            call._participants.Add(new CallParticipant(
                memberId,
                memberId == initiatorId ? CallParticipantStatus.Accepted : CallParticipantStatus.Invited,
                now));
        }

        return Result<Call>.Ok(call);
    }

    /// <summary>Accepts the invite. Members who joined the chat mid-call are added.</summary>
    public Result Accept(string userId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!IsLive)
        {
            return Result.Fail(CallErrors.Ended);
        }

        var participant = GetParticipant(userId);
        if (participant is null)
        {
            _participants.Add(new CallParticipant(userId, CallParticipantStatus.Accepted, now));
            return Result.Ok();
        }

        if (participant.Status != CallParticipantStatus.Accepted)
        {
            participant.Status = CallParticipantStatus.Accepted;
        }

        return Result.Ok();
    }

    public Result Reject(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!IsLive)
        {
            return Result.Fail(CallErrors.Ended);
        }

        var participant = GetParticipant(userId);
        if (participant is null)
        {
            return Result.Fail(CallErrors.Forbidden);
        }

        participant.Status = CallParticipantStatus.Rejected;
        participant.IsJoined = false;
        return Result.Ok();
    }

    /// <summary>
    /// Marks media presence (client connected to the LiveKit room). The first
    /// join flips the call live; joining implies acceptance.
    /// </summary>
    public Result MarkJoined(string userId, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!IsLive)
        {
            return Result.Fail(CallErrors.Ended);
        }

        var participant = GetParticipant(userId);
        if (participant is null)
        {
            participant = new CallParticipant(userId, CallParticipantStatus.Accepted, now);
            _participants.Add(participant);
        }
        else
        {
            participant.Status = CallParticipantStatus.Accepted;
        }

        participant.IsJoined = true;
        participant.JoinedAt = now;
        if (Status == CallStatus.Ringing)
        {
            Status = CallStatus.Active;
            StartedAt = now;
        }

        return Result.Ok();
    }

    /// <summary>Marks media departure. Returns true when nobody remains joined.</summary>
    public bool MarkLeft(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var participant = GetParticipant(userId);
        if (participant is not null)
        {
            participant.IsJoined = false;
        }

        return IsLive && _participants.All(p => !p.IsJoined);
    }

    public Result End(string endedBy, CallEndReason reason, DateTime now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endedBy);
        if (!IsLive)
        {
            return Result.Fail(CallErrors.Ended);
        }

        Status = CallStatus.Ended;
        EndedAt = now;
        EndedBy = endedBy;
        EndReason = reason;
        foreach (var participant in _participants)
        {
            participant.IsJoined = false;
        }

        return Result.Ok();
    }

    /// <summary>
    /// Persistence rehydration. Bypasses <see cref="Create"/> (no re-validation
    /// of already-stored state). Infrastructure use only.
    /// </summary>
    public static Call Rehydrate(CallSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var call = new Call(
            snapshot.Id, snapshot.ChatId, snapshot.InitiatorId,
            snapshot.MediaType, snapshot.RoomName, snapshot.CreatedAt);
        call.Status = snapshot.Status;
        call.StartedAt = snapshot.StartedAt;
        call.EndedAt = snapshot.EndedAt;
        call.EndedBy = snapshot.EndedBy;
        call.EndReason = snapshot.EndReason;
        foreach (var p in snapshot.Participants)
        {
            call._participants.Add(new CallParticipant(p.UserId, p.Status, p.InvitedAt)
            {
                IsJoined = p.IsJoined,
                JoinedAt = p.JoinedAt
            });
        }

        return call;
    }
}
