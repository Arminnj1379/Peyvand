using Messenger.Application.Calls;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Call signaling: start/accept/reject/join/leave/end plus queries.</summary>
public interface ICallService
{
    Task<Result<CallDto>> StartCallAsync(StartCallCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<CallDto>> GetCallAsync(GetCallQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<CallDto>>> ListCallsAsync(ListCallsQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<CallDto>> AcceptCallAsync(AcceptCallCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<CallDto>> RejectCallAsync(RejectCallCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<CallJoinDto>> JoinCallAsync(JoinCallCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<CallDto>> LeaveCallAsync(LeaveCallCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<CallDto>> EndCallAsync(EndCallCommand command, CallerContext caller, CancellationToken ct);
}
