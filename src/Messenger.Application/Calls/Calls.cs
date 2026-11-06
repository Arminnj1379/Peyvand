using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Messaging;
using Messenger.Domain.Calls;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Calls;

/// <summary>
/// Call signaling handlers. Every operation re-verifies chat membership
/// server-side; the JWT subject is the only identity. Media flows through
/// LiveKit — this slice only tracks who is invited/joined/left/ended.
/// </summary>
public sealed record StartCallCommand(string ChatId, string MediaType) : IRequest<Result<CallDto>>;

public sealed class StartCallValidator : AbstractValidator<StartCallCommand>
{
    public StartCallValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MediaType).NotEmpty().Must(v =>
            v.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
            v.Equals("Video", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Call type must be audio or video.");
    }
}

public sealed class StartCallHandler(
    IChatStore chats,
    ICallStore calls,
    IIdGenerator ids,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<StartCallCommand, Result<CallDto>>
{
    public async Task<Result<CallDto>> Handle(StartCallCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        // Channels broadcast: only posters may ring the channel. Everyone else
        // gets CannotStart (403), not a membership leak.
        var authorized = chat?.Type == ChatType.Channel
            ? ChatAccess.RequireCanPost(chat, currentUser.UserId)
            : ChatAccess.RequireMember(chat, currentUser.UserId);
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

        var mediaType = request.MediaType.Equals("Video", StringComparison.OrdinalIgnoreCase)
            ? CallMediaType.Video
            : CallMediaType.Audio;
        var created = Call.Create(
            ids.NewId(), chat.Id, currentUser.UserId, mediaType,
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
}

public sealed record GetCallQuery(string CallId) : IRequest<Result<CallDto>>;

public sealed class GetCallValidator : AbstractValidator<GetCallQuery>
{
    public GetCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetCallHandler(
    IChatStore chats,
    ICallStore calls,
    ICurrentUser currentUser) : IRequestHandler<GetCallQuery, Result<CallDto>>
{
    public async Task<Result<CallDto>> Handle(GetCallQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(request.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }
}

public sealed record ListCallsQuery(string ChatId, int Limit) : IRequest<Result<IReadOnlyList<CallDto>>>;

public sealed class ListCallsValidator : AbstractValidator<ListCallsQuery>
{
    public ListCallsValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class ListCallsHandler(
    IChatStore chats,
    ICallStore calls,
    ICurrentUser currentUser) : IRequestHandler<ListCallsQuery, Result<IReadOnlyList<CallDto>>>
{
    public async Task<Result<IReadOnlyList<CallDto>>> Handle(ListCallsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<CallDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<IReadOnlyList<CallDto>>.Fail(CallErrors.Forbidden);
        }

        var found = await calls.ListForChatAsync(chat!.Id, request.Limit, ct).ConfigureAwait(false);
        return Result<IReadOnlyList<CallDto>>.Ok(found.Select(CallMapping.ToDto).ToList());
    }
}

public sealed record AcceptCallCommand(string CallId) : IRequest<Result<CallDto>>;

public sealed class AcceptCallValidator : AbstractValidator<AcceptCallCommand>
{
    public AcceptCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed class AcceptCallHandler(
    IChatStore chats,
    ICallStore calls,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<AcceptCallCommand, Result<CallDto>>
{
    public async Task<Result<CallDto>> Handle(AcceptCallCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(request.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, currentUser.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        var accepted = call.Accept(currentUser.UserId, clock.UtcNow);
        if (accepted.IsFailure)
        {
            return Result<CallDto>.Fail(accepted.Error);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }
}

public sealed record RejectCallCommand(string CallId) : IRequest<Result<CallDto>>;

public sealed class RejectCallValidator : AbstractValidator<RejectCallCommand>
{
    public RejectCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed class RejectCallHandler(
    IChatStore chats,
    ICallStore calls,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<RejectCallCommand, Result<CallDto>>
{
    public async Task<Result<CallDto>> Handle(RejectCallCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(request.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, currentUser.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        var rejected = call.Reject(currentUser.UserId);
        if (rejected.IsFailure)
        {
            return Result<CallDto>.Fail(rejected.Error);
        }

        // A declined 1-1 ring is over: nobody left to talk to.
        if (chat!.Type == ChatType.Dm)
        {
            call.End(currentUser.UserId, CallEndReason.Declined, clock.UtcNow);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }
}

public sealed record JoinCallCommand(string CallId) : IRequest<Result<CallJoinDto>>;

public sealed class JoinCallValidator : AbstractValidator<JoinCallCommand>
{
    public JoinCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed class JoinCallHandler(
    IChatStore chats,
    ICallStore calls,
    IUserStore users,
    ILiveKitTokenService liveKit,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<JoinCallCommand, Result<CallJoinDto>>
{
    public async Task<Result<CallJoinDto>> Handle(JoinCallCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<CallJoinDto>.Fail(AuthErrors.Unauthenticated);
        }

        // Fail before mutating: no token means no join (client retries later).
        if (!liveKit.IsConfigured)
        {
            return Result<CallJoinDto>.Fail(CallErrors.LiveKitNotConfigured);
        }

        var call = await calls.GetByIdAsync(request.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallJoinDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, currentUser.UserId).IsFailure)
        {
            return Result<CallJoinDto>.Fail(CallErrors.Forbidden);
        }

        var joined = call.MarkJoined(currentUser.UserId, clock.UtcNow);
        if (joined.IsFailure)
        {
            return Result<CallJoinDto>.Fail(joined.Error);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        var displayName = user?.DisplayName ?? user?.Username ?? "Unknown";
        var token = liveKit.CreateJoinToken(call.RoomName, currentUser.UserId, displayName);
        return Result<CallJoinDto>.Ok(new CallJoinDto(
            CallMapping.ToDto(call), token, liveKit.ServerUrl, call.RoomName, currentUser.UserId));
    }
}

public sealed record LeaveCallCommand(string CallId) : IRequest<Result<CallDto>>;

public sealed class LeaveCallValidator : AbstractValidator<LeaveCallCommand>
{
    public LeaveCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

public sealed class LeaveCallHandler(
    IChatStore chats,
    ICallStore calls,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<LeaveCallCommand, Result<CallDto>>
{
    public async Task<Result<CallDto>> Handle(LeaveCallCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(request.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, currentUser.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        if (!call.IsLive)
        {
            return Result<CallDto>.Fail(CallErrors.Ended);
        }

        // Last one out hangs up for everyone (DM or emptied group call).
        if (call.MarkLeft(currentUser.UserId))
        {
            call.End(currentUser.UserId, CallEndReason.Completed, clock.UtcNow);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }
}

public sealed record EndCallCommand(string CallId) : IRequest<Result<CallDto>>;

public sealed class EndCallValidator : AbstractValidator<EndCallCommand>
{
    public EndCallValidator()
    {
        RuleFor(x => x.CallId).NotEmpty().MaximumLength(64);
    }
}

/// <summary>
/// Ends a call for everyone. DMs: either party may hang up. Groups/channels:
/// the caller or a chat admin (prevents strangers from killing the call).
/// </summary>
public sealed class EndCallHandler(
    IChatStore chats,
    ICallStore calls,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<EndCallCommand, Result<CallDto>>
{
    public async Task<Result<CallDto>> Handle(EndCallCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<CallDto>.Fail(AuthErrors.Unauthenticated);
        }

        var call = await calls.GetByIdAsync(request.CallId, ct).ConfigureAwait(false);
        if (call is null)
        {
            return Result<CallDto>.Fail(CallErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(call.ChatId, ct).ConfigureAwait(false);
        if (ChatAccess.RequireMember(chat, currentUser.UserId).IsFailure)
        {
            return Result<CallDto>.Fail(CallErrors.Forbidden);
        }

        var canEnd = chat!.Type == ChatType.Dm
            || call.InitiatorId == currentUser.UserId
            || chat.IsAdmin(currentUser.UserId);
        if (!canEnd)
        {
            return Result<CallDto>.Fail(CallErrors.NotInitiator);
        }

        var reason = call.Status == CallStatus.Ringing ? CallEndReason.Cancelled : CallEndReason.Completed;
        var ended = call.End(currentUser.UserId, reason, clock.UtcNow);
        if (ended.IsFailure)
        {
            return Result<CallDto>.Fail(ended.Error);
        }

        await calls.UpdateAsync(call, ct).ConfigureAwait(false);
        return Result<CallDto>.Ok(CallMapping.ToDto(call));
    }
}
