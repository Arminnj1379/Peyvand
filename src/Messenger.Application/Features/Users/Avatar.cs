using FluentValidation;

namespace Messenger.Application.Features.Users;

/// <summary>
/// Avatar upload. Bytes are sniffed (magic numbers), not just the declared
/// content type.</summary>
public sealed record UploadAvatarCommand(byte[] Bytes, string ContentType)
{
    public const int MaxBytes = 2 * 1024 * 1024;
}

public sealed record RemoveAvatarCommand();

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
