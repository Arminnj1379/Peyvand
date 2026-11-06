using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Users;

/// <summary>Serves stored avatar bytes. Same visibility rule as profile reads.</summary>
public sealed record GetAvatarQuery(string UserId) : IRequest<Result<AvatarFileDto>>;

public sealed record AvatarFileDto(byte[] Bytes, string ContentType, string ETag);

public sealed class GetAvatarValidator : AbstractValidator<GetAvatarQuery>
{
    public GetAvatarValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetAvatarHandler(
    IUserStore users,
    IAvatarStorage avatars,
    IChatStore chats,
    ICurrentUser currentUser) : IRequestHandler<GetAvatarQuery, Result<AvatarFileDto>>
{
    public async Task<Result<AvatarFileDto>> Handle(GetAvatarQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<AvatarFileDto>.Fail(AuthErrors.Unauthenticated);
        }

        var target = await users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive || target.AvatarObjectKey is null)
        {
            return Result<AvatarFileDto>.Fail(UserErrors.NotFound);
        }

        if (!await ProfileVisibility.CanViewAsync(currentUser, target, chats, ct).ConfigureAwait(false))
        {
            return Result<AvatarFileDto>.Fail(UserErrors.NotFound);
        }

        var file = await avatars.LoadAsync(target.AvatarObjectKey, ct).ConfigureAwait(false);
        if (file is null)
        {
            return Result<AvatarFileDto>.Fail(UserErrors.NotFound);
        }

        return Result<AvatarFileDto>.Ok(new AvatarFileDto(file.Value.Bytes, file.Value.ContentType, target.AvatarObjectKey));
    }
}
