using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Users;

/// <summary>
/// Avatar upload. Bytes are sniffed (magic numbers), not just the declared
/// content type. Storage is behind <see cref="IAvatarStorage"/>.
/// </summary>
public sealed record UploadAvatarCommand(byte[] Bytes, string ContentType) : IRequest<Result<UserDto>>
{
    public const int MaxBytes = 2 * 1024 * 1024;
}

public sealed record RemoveAvatarCommand() : IRequest<Result<UserDto>>;

public sealed class UploadAvatarValidator : AbstractValidator<UploadAvatarCommand>
{
    public UploadAvatarValidator()
    {
        RuleFor(x => x.Bytes).NotEmpty().Must(b => b.Length <= UploadAvatarCommand.MaxBytes)
            .WithMessage("Avatar must be 2 MB or smaller.");
        RuleFor(x => x.ContentType).Must(ct =>
                ct == "image/jpeg" || ct == "image/png" || ct == "image/webp")
            .WithMessage("Avatar must be a JPEG, PNG, or WebP image.");
    }
}

public sealed class UploadAvatarHandler(
    IUserStore users,
    IAvatarStorage avatars,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<UploadAvatarCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(UploadAvatarCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        if (!HasValidMagicNumber(request.Bytes, request.ContentType))
        {
            return Result<UserDto>.Fail(UserErrors.AvatarUnsupportedType);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        var reference = await avatars.SaveAsync(user.Id, request.ContentType, request.Bytes, ct)
            .ConfigureAwait(false);

        if (user.AvatarObjectKey is not null && user.AvatarObjectKey != reference.ObjectKey)
        {
            await avatars.DeleteAsync(user.AvatarObjectKey, ct).ConfigureAwait(false);
        }

        var url = $"/api/v1/users/{user.Id}/avatar?v={Uri.EscapeDataString(reference.ObjectKey)}";
        user.SetAvatar(reference.ObjectKey, url, clock.UtcNow);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }

    internal static bool HasValidMagicNumber(byte[] bytes, string contentType)
    {
        if (bytes.Length < 12)
        {
            return false;
        }

        return contentType switch
        {
            "image/jpeg" => bytes[0] == 0xFF && bytes[1] == 0xD8,
            "image/png" => bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47,
            // RIFF....WEBP
            "image/webp" => bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
                && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50,
            _ => false
        };
    }
}

public sealed class RemoveAvatarHandler(
    IUserStore users,
    IAvatarStorage avatars,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<RemoveAvatarCommand, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(RemoveAvatarCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (user.AvatarObjectKey is not null)
        {
            await avatars.DeleteAsync(user.AvatarObjectKey, ct).ConfigureAwait(false);
        }

        user.RemoveAvatar(clock.UtcNow);
        await users.UpdateAsync(user, ct).ConfigureAwait(false);
        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }
}
