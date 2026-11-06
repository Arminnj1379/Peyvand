using FluentValidation;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Chat avatars. Admin-only for groups/channels (DMs have no shared avatar —
/// peers already have user avatars). Bytes ride the shared avatar storage port
/// under a <c>chat-{chatId}</c> key.
/// </summary>
public sealed record UploadChatAvatarCommand(string ChatId, byte[] Bytes, string ContentType)
{
    public const int MaxBytes = 2 * 1024 * 1024;
}

public sealed record RemoveChatAvatarCommand(string ChatId);

public sealed record GetChatAvatarQuery(string ChatId);

public sealed class UploadChatAvatarValidator : AbstractValidator<UploadChatAvatarCommand>
{
    public UploadChatAvatarValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Bytes).NotEmpty().Must(b => b.Length <= UploadChatAvatarCommand.MaxBytes)
            .WithMessage("Avatar must be 2 MB or smaller.");
        RuleFor(x => x.ContentType).Must(ct =>
                ct == "image/jpeg" || ct == "image/png" || ct == "image/webp")
            .WithMessage("Avatar must be a JPEG, PNG, or WebP image.");
    }
}

public sealed class RemoveChatAvatarValidator : AbstractValidator<RemoveChatAvatarCommand>
{
    public RemoveChatAvatarValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetChatAvatarValidator : AbstractValidator<GetChatAvatarQuery>
{
    public GetChatAvatarValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}
