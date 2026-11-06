using Messenger.Application.Features.Admin;
using Messenger.Application.Features.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Internal employee management (administrators only).</summary>
public interface IAdminService
{
    Task<Result<UserDto>> CreateEmployeeAsync(
        CreateEmployeeCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<UserDto>>> ListUsersAsync(
        ListUsersQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<UserDto>> SetUserActiveAsync(
        SetUserActiveCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<UserDto>> SetUserRolesAsync(
        SetUserRolesCommand command, CallerContext caller, CancellationToken ct);
}
