using Messenger.Application.Auth;
using Messenger.Application.Features.Auth;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Registration, login, session rotation, logout, password change.</summary>
public interface IAuthService
{
    Task<Result<AuthResultDto>> RegisterAsync(RegisterCustomerCommand command, CancellationToken ct);

    Task<Result<AuthResultDto>> LoginAsync(LoginCommand command, CancellationToken ct);

    Task<Result<AuthResultDto>> RefreshAsync(RefreshSessionCommand command, CancellationToken ct);

    Task<Result> LogoutAsync(CallerContext caller, CancellationToken ct);

    Task<Result> LogoutAllAsync(CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<SessionDto>>> ListSessionsAsync(CallerContext caller, CancellationToken ct);

    Task<Result> RevokeSessionAsync(RevokeSessionCommand command, CallerContext caller, CancellationToken ct);

    Task<Result> ChangePasswordAsync(ChangePasswordCommand command, CallerContext caller, CancellationToken ct);
}
