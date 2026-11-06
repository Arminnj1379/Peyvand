using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Chat avatars. Admin-only for groups/channels (DMs have no shared avatar —
/// peers already have user avatars). Bytes ride the shared
/// <see cref="IAvatarStorage"/> port under a <c>chats/{chatId}</c> key so the
/// future Media/MinIO swap stays internal to Infrastructure.
/// </summary>
public sealed record UploadChatAvatarCommand(string ChatId, byte[] Bytes, string ContentType)
    : IRequest<Result<ChatDto>>
{
    public const int MaxBytes = 2 * 1024 * 1024;
}

public sealed record RemoveChatAvatarCommand(string ChatId) : IRequest<Result<ChatDto>>;

public sealed record GetChatAvatarQuery(string ChatId) : IRequest<Result<(byte[] Bytes, string ContentType)>>;

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

public sealed class UploadChatAvatarHandler(
    IChatStore chats,
    IUserStore users,
    IAvatarStorage avatars,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<UploadChatAvatarCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(UploadChatAvatarCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        if (!HasValidMagicNumber(request.Bytes, request.ContentType))
        {
            return Result<ChatDto>.Fail(ChatErrors.InvalidAvatar);
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        if (admin.Value!.Type == ChatType.Dm)
        {
            return Result<ChatDto>.Fail(ChatErrors.DmMembershipFixed);
        }

        var reference = await avatars.SaveAsync(
            $"chat-{admin.Value.Id}", request.ContentType, request.Bytes, ct).ConfigureAwait(false);

        if (admin.Value.AvatarObjectKey is not null && admin.Value.AvatarObjectKey != reference.ObjectKey)
        {
            await avatars.DeleteAsync(admin.Value.AvatarObjectKey, ct).ConfigureAwait(false);
        }

        var url = $"/api/v1/chats/{admin.Value.Id}/avatar?v={Uri.EscapeDataString(reference.ObjectKey)}";
        admin.Value.SetAvatar(reference.ObjectKey, url, clock.UtcNow);
        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
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

public sealed class RemoveChatAvatarHandler(
    IChatStore chats,
    IUserStore users,
    IAvatarStorage avatars,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<RemoveChatAvatarCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(RemoveChatAvatarCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, currentUser.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        if (admin.Value!.AvatarObjectKey is not null)
        {
            await avatars.DeleteAsync(admin.Value.AvatarObjectKey, ct).ConfigureAwait(false);
        }

        admin.Value.RemoveAvatar(clock.UtcNow);
        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, currentUser.UserId, ct).ConfigureAwait(false));
    }
}

public sealed class GetChatAvatarHandler(
    IChatStore chats,
    IAvatarStorage avatars,
    ICurrentUser currentUser) : IRequestHandler<GetChatAvatarQuery, Result<(byte[] Bytes, string ContentType)>>
{
    public async Task<Result<(byte[] Bytes, string ContentType)>> Handle(GetChatAvatarQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<(byte[] Bytes, string ContentType)>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        // DMs and private chats: members only. Public channels: anyone (avatar is public).
        if (chat is null || chat.IsDeleted)
        {
            return Result<(byte[] Bytes, string ContentType)>.Fail(ChatErrors.NotFound);
        }

        var isPublicChannel = chat.Type == ChatType.Channel && chat.IsPublic;
        if (!isPublicChannel && !chat.IsMember(currentUser.UserId))
        {
            return Result<(byte[] Bytes, string ContentType)>.Fail(ChatErrors.Forbidden);
        }

        if (chat.AvatarObjectKey is null)
        {
            return Result<(byte[] Bytes, string ContentType)>.Fail(ChatErrors.NotFound);
        }

        var loaded = await avatars.LoadAsync(chat.AvatarObjectKey, ct).ConfigureAwait(false);
        if (loaded is null)
        {
            return Result<(byte[] Bytes, string ContentType)>.Fail(ChatErrors.NotFound);
        }

        return Result<(byte[] Bytes, string ContentType)>.Ok(loaded.Value);
    }
}
