using Messenger.Application.Features.Users;
using Messenger.Application.Users;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Self profile, directory search, avatars, settings, blocking.</summary>
public interface IUserService
{
    Task<Result<UserDto>> GetMyProfileAsync(CallerContext caller, CancellationToken ct);

    Task<Result<UserDto>> UpdateMyProfileAsync(UpdateMyProfileCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<UserDto>> UploadAvatarAsync(UploadAvatarCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<UserDto>> RemoveAvatarAsync(CallerContext caller, CancellationToken ct);

    Task<Result<SettingsDto>> GetMySettingsAsync(CallerContext caller, CancellationToken ct);

    Task<Result<SettingsDto>> UpdateMySettingsAsync(UpdateMySettingsCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<IReadOnlyList<UserDto>>> SearchUsersAsync(SearchUsersQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<UserDto>> GetUserByIdAsync(GetUserByIdQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<AvatarFileDto>> GetAvatarAsync(GetAvatarQuery query, CallerContext caller, CancellationToken ct);

    Task<Result> BlockUserAsync(BlockUserCommand command, CallerContext caller, CancellationToken ct);

    Task<Result> UnblockUserAsync(UnblockUserCommand command, CallerContext caller, CancellationToken ct);
}
