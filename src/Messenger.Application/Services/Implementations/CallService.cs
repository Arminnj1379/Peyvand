using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Calls;
using Messenger.Application.Messaging;
using Messenger.Application.Services.Interfaces;
using Messenger.Domain.Calls;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Implementations;

/// <summary>Call signaling use cases. Logic moved verbatim from the former MediatR handlers.</summary>
public sealed class CallService(
    IChatStore chats,
    ICallStore calls,
    IUserStore users,
    ILiveKitTokenService liveKit,
    IIdGenerator ids,
    IClock clock) : ICallService
{
    public async Task<Result<CallDto>> StartCallAsync(
        StartCallCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new StartCallValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        // Channels broadcast: only posters may ring the channel. Everyone else
        // gets CannotStart (403), not a membership leak.
        var authorized = chat?.Type == ChatType.Channel
            ? ChatAccess.RequireCanPost(chat, caller.UserId)
            : ChatAccess.RequireMember(chat, caller.UserId);
        if (authorized.IsFailure)
        {
            return authorized.Error.Code == ChatErrors.NotFound.Code
                ? Result<CallDto>.Fail(authorized.Error)
                : Result<CallDto>.Fail(CallErrors.CannotStart);
        }

        var live = await calls.GetActiveForChatAsync(chat!.Id, ct).ConfigureAwait(false);
        if (live is not null)
        {
            return Result<CallDto>.Fail(CallErrors.AlreadyActive);
        }

        var mediaType = command.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase)
            ? CallMediaType.Video
            : CallMediaType.Audio;
        var created = Call.Create(
            ids.NewId(), chat.Id, caller.UserId, mediaType,
            chat.Members.Select(m => m.UserId), clock.UtcNow);
        if (created.IsFailure)
        {
            return Result<CallDto>.Fail(created.Error);
        }

        try
        {
            await calls.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateCallException)
        {
            // Lost the insert race on the live-call guard: the winner owns the
            // ring — report busy instead of stacking a second call.
            return Result<CallDto>.Fail(CallErrors.AlreadyActive);
        }

        return Result<CallDto>.Ok(CallMapping.ToDto(created.Value!));
    }

    public async Task<Result<CallDto>> GetCallAsync(
        GetCallQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetCallValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(query.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }

    public async Task<Result<IReadOnlyList<CallDto>>> ListCallsAsync(
        ListCallsQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new ListCallsValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<CallDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var chat = await chats.GetByIdAsync(query.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<IReadOnlyList<CallDto>>.Fail(CallErrors.Forbidden);
        }

        var found = await calls.ListForChatAsync(chat!.Id, query.Limit, ct).ConfigureAwait(false);
        return Result<IReadOnlyList<CallDto>>.Ok(found.Select(CallMapping.ToDto).ToList());
    }

    public async Task<Result<CallDto>> AcceptCallAsync(
        AcceptCallCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new AcceptCallValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(command.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, caller.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        var accepted = call.Accept(caller.UserId, clock.UtcNow);
        if (accepted.IsFailure)
        {
            return Result<CallDto>.Fail(accepted.Error);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }

    public async Task<Result<CallDto>> RejectCallAsync(
        RejectCallCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new RejectCallValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(command.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, caller.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        var rejected = call.Reject(caller.UserId);
        if (rejected.IsFailure)
        {
            return Result<CallDto>.Fail(rejected.Error);
        }

        // A declined 1-1 ring is over: nobody left to talk to.
        if (chat!.Type == ChatType.Dm)
        {
            call.End(caller.UserId, CallEndReason.Declined, clock.UtcNow);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }

    public async Task<Result<CallJoinDto>> JoinCallAsync(
        JoinCallCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new JoinCallValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<CallJoinDto>.Fail(AuthErrors.Unauthenticated);
        }

        // Fail before mutating: no token means no join (client retries later).
        if (!liveKit.IsConfigured)
        {
            return Result<CallJoinDto>.Fail(CallErrors.LiveKitNotConfigured);
        }

        var call = await calls.GetByIdAsync(command.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallJoinDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, caller.UserId).IsFailure)
        {
            return Result<CallJoinDto>.Fail(CallErrors.Forbidden);
        }

        var joined = call.MarkJoined(caller.UserId, clock.UtcNow);
        if (joined.IsFailure)
        {
            return Result<CallJoinDto>.Fail(joined.Error);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        var displayName = user?.DisplayName ?? user?.Username ?? "Unknown";
        var token = liveKit.CreateJoinToken(call.RoomName, caller.UserId, displayName);
        return Result<CallJoinDto>.Ok(new CallJoinDto(
            CallMapping.ToDto(call), token, liveKit.ServerUrl, call.RoomName, caller.UserId));
    }

    public async Task<Result<CallDto>> LeaveCallAsync(
        LeaveCallCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new LeaveCallValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(command.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, caller.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        if (!call.IsLive)
        {
            return Result<CallDto>.Fail(CallErrors.Ended);
        }

        // Last one out hangs up for everyone (DM or emptied group call).
        if (call.MarkLeft(caller.UserId))
        {
            call.End(caller.UserId, CallEndReason.Completed, clock.UtcNow);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }

    public async Task<Result<CallDto>> EndCallAsync(
        EndCallCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new EndCallValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(command.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, caller.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        var canEnd = chat!.Type == ChatType.Dm
            || call.InitiatorId == caller.UserId
            || chat.IsAdmin(caller.UserId);
        if (!canEnd)
        {
            return Result<CallDto>.Fail(CallErrors.NotInitiator);
        }

        var reason = call.Status == CallStatus.Ringing ? CallEndReason.Cancelled : CallEndReason.Completed;
        var ended = call.End(caller.UserId, reason, clock.UtcNow);
        if (ended.IsFailure)
        {
            return Result<CallDto>.Fail(ended.Error);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }
}
