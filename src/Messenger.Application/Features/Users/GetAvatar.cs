using FluentValidation;

namespace Messenger.Application.Features.Users;

/// <summary>Serves stored avatar bytes. Same visibility rule as profile reads.</summary>
public sealed record GetAvatarQuery(string UserId);

public sealed record AvatarFileDto(byte[] Bytes, string ContentType, string ETag);

public sealed class GetAvatarValidator : AbstractValidator<GetAvatarQuery>
{
    public GetAvatarValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}
