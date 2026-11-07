using System.Security.Cryptography;
using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Application.Services.Interfaces;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;
using Messenger.Domain.Users;

namespace Messenger.Application.Services.Implementations;

/// <summary>
/// Group collaboration. Membership changes (add/remove/leave/join) record an
/// English system message in history so the change is visible to members.
/// </summary>
public sealed class GroupService(
    IChatStore chats,
    IUserStore users,
    IAvatarStorage avatars,
    IMessageStore messages,
    IFileStore files,
    IIdGenerator ids,
    IClock clock) : IGroupService
{
    public async Task<Result<MembershipChangeResult>> AddMemberAsync(
        AddMemberCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new AddMemberValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MembershipChangeResult>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(member.Error);
        }

        // Admins always; plain members only when the group allows member invites.
        // (Channels never allow subscriber invites — admins only.)
        if (!member.Value!.CanInvite(caller.UserId))
        {
            return Result<MembershipChangeResult>.Fail(ChatErrors.NotAdmin);
        }

        var admin = member;

        var target = await users.GetByIdAsync(command.UserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive)
        {
            return Result<MembershipChangeResult>.Fail(UserErrors.NotFound);
        }

        var role = chat!.Type == ChatType.Channel ? ChatRole.Subscriber : ChatRole.Member;
        var added = admin.Value!.AddMember(target.Id, role, clock.UtcNow);
        if (added.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(added.Error);
        }

        var adder = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        var system = await AppendSystemMessageAsync(
            admin.Value, $"{adder?.DisplayName ?? "Someone"} added {target.DisplayName}",
            caller.UserId, ct).ConfigureAwait(false);
        if (system.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(system.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<MembershipChangeResult>.Ok(new MembershipChangeResult(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false),
            system.Value!));
    }

    public async Task<Result<MembershipChangeResult>> RemoveMemberAsync(
        RemoveMemberCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new RemoveMemberValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MembershipChangeResult>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(admin.Error);
        }

        var target = await users.GetByIdAsync(command.UserId, ct).ConfigureAwait(false);
        var removed = admin.Value!.RemoveMember(command.UserId, clock.UtcNow);
        if (removed.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(removed.Error);
        }

        var actor = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        var system = await AppendSystemMessageAsync(
            admin.Value, $"{actor?.DisplayName ?? "Someone"} removed {target?.DisplayName ?? "a member"}",
            caller.UserId, ct).ConfigureAwait(false);
        if (system.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(system.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<MembershipChangeResult>.Ok(new MembershipChangeResult(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false),
            system.Value!));
    }

    public async Task<Result<MembershipChangeResult>> LeaveChatAsync(
        LeaveChatCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new LeaveChatValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MembershipChangeResult>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(member.Error);
        }

        var leaver = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        var left = member.Value!.RemoveMember(caller.UserId, clock.UtcNow);
        if (left.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(left.Error);
        }

        var noun = member.Value.Type == ChatType.Channel ? "channel" : "group";
        var system = await AppendSystemMessageAsync(
            member.Value, $"{leaver?.DisplayName ?? "Someone"} left the {noun}",
            caller.UserId, ct).ConfigureAwait(false);
        if (system.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(system.Error);
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result<MembershipChangeResult>.Ok(new MembershipChangeResult(
            await ChatQueries.EnrichAsync(chats, users, member.Value, caller.UserId, ct).ConfigureAwait(false),
            system.Value!));
    }

    public async Task<Result<ChatDto>> SetMemberRoleAsync(
        SetMemberRoleCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SetMemberRoleValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        if (!Enum.TryParse<ChatRole>(command.Role, ignoreCase: true, out var role))
        {
            return Result<ChatDto>.Fail(ChatErrors.InvalidRole);
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        var updated = admin.Value!.SetMemberRole(command.UserId, role, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<string?>> GetInviteAsync(
        GetInviteQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetInviteValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<string?>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(query.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<string?>.Fail(member.Error);
        }

        return Result<string?>.Ok(member.Value!.InviteCode);
    }

    public async Task<Result<string>> RotateInviteAsync(
        RotateInviteCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new RotateInviteValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<string>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result<string>.Fail(admin.Error);
        }

        if (admin.Value!.Type == ChatType.Dm)
        {
            return Result<string>.Fail(ChatErrors.DmMembershipFixed);
        }

        // Rotation loop is bounded: codes are 128-bit, collisions are theoretical.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
            var clash = await chats.GetByInviteCodeAsync(code, ct).ConfigureAwait(false);
            if (clash is not null && clash.Id != admin.Value.Id)
            {
                continue;
            }

            var rotated = admin.Value.RotateInviteCode(code, clock.UtcNow);
            if (rotated.IsFailure)
            {
                return Result<string>.Fail(rotated.Error);
            }

            try
            {
                await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
            }
            catch (DuplicateChatException)
            {
                continue;
            }

            return Result<string>.Ok(code);
        }

        return Result<string>.Fail(ChatErrors.MemberLimit);
    }

    public async Task<Result> RevokeInviteAsync(
        RevokeInviteCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new RevokeInviteValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result.Fail(admin.Error);
        }

        admin.Value!.RevokeInviteCode(clock.UtcNow);
        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result<MembershipChangeResult>> JoinViaInviteAsync(
        JoinViaInviteCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new JoinViaInviteValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<MembershipChangeResult>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = caller.UserId;
        var self = await users.GetByIdAsync(callerId, ct).ConfigureAwait(false);
        if (self is null || !self.IsActive)
        {
            return Result<MembershipChangeResult>.Fail(UserErrors.NotFound);
        }

        var chat = await chats.GetByInviteCodeAsync(command.Code.Trim(), ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type == ChatType.Dm)
        {
            // Neutral: revoked and never-existed codes look identical.
            return Result<MembershipChangeResult>.Fail(ChatErrors.InviteInvalid);
        }

        if (chat.IsMember(callerId))
        {
            return Result<MembershipChangeResult>.Ok(new MembershipChangeResult(
                await ChatQueries.EnrichAsync(chats, users, chat, callerId, ct).ConfigureAwait(false), null));
        }

        var role = chat.Type == ChatType.Channel ? ChatRole.Subscriber : ChatRole.Member;
        var added = chat.AddMember(callerId, role, clock.UtcNow);
        if (added.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(added.Error);
        }

        var noun = chat.Type == ChatType.Channel ? "channel" : "group";
        var system = await AppendSystemMessageAsync(
            chat, $"{self.DisplayName} joined the {noun}", callerId, ct).ConfigureAwait(false);
        if (system.IsFailure)
        {
            return Result<MembershipChangeResult>.Fail(system.Error);
        }

        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result<MembershipChangeResult>.Ok(new MembershipChangeResult(
            await ChatQueries.EnrichAsync(chats, users, chat, callerId, ct).ConfigureAwait(false),
            system.Value!));
    }

    public async Task<Result<ChatDto>> SubscribeChannelAsync(
        SubscribeChannelCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SubscribeChannelValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = caller.UserId;
        var self = await users.GetByIdAsync(callerId, ct).ConfigureAwait(false);
        if (self is null || !self.IsActive)
        {
            return Result<ChatDto>.Fail(UserErrors.NotFound);
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type != ChatType.Channel)
        {
            return Result<ChatDto>.Fail(ChatErrors.NotFound);
        }

        if (chat.IsMember(callerId))
        {
            return Result<ChatDto>.Fail(ChatErrors.AlreadySubscribed);
        }

        if (!chat.IsPublic)
        {
            return Result<ChatDto>.Fail(ChatErrors.InviteRequired);
        }

        var added = chat.AddMember(callerId, ChatRole.Subscriber, clock.UtcNow);
        if (added.IsFailure)
        {
            return Result<ChatDto>.Fail(added.Error);
        }

        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, chat, callerId, ct).ConfigureAwait(false));
    }

    public async Task<Result> UnsubscribeChannelAsync(
        UnsubscribeChannelCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UnsubscribeChannelValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type != ChatType.Channel)
        {
            return Result.Fail(ChatErrors.NotFound);
        }

        if (!chat.IsMember(caller.UserId))
        {
            return Result.Fail(ChatErrors.NotSubscribed);
        }

        var left = chat.RemoveMember(caller.UserId, clock.UtcNow);
        if (left.IsFailure)
        {
            return left;
        }

        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result.Ok();
    }

    public async Task<Result<PublicChannelDto>> GetPublicChannelAsync(
        GetPublicChannelQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        await new GetPublicChannelValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        var chat = await chats.GetByUsernameAsync(query.Username.Trim(), ct).ConfigureAwait(false);
        if (chat is null || chat.IsDeleted || chat.Type != ChatType.Channel || !chat.IsPublic)
        {
            return Result<PublicChannelDto>.Fail(ChatErrors.NotFound);
        }

        return Result<PublicChannelDto>.Ok(MessagingMapping.ToPublicDto(chat));
    }

    public async Task<Result<ChatDto>> UploadChatAvatarAsync(
        UploadChatAvatarCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UploadChatAvatarValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        if (!AvatarValidation.HasValidMagicNumber(command.Bytes, command.ContentType))
        {
            return Result<ChatDto>.Fail(ChatErrors.InvalidAvatar);
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        if (admin.Value!.Type == ChatType.Dm)
        {
            return Result<ChatDto>.Fail(ChatErrors.DmMembershipFixed);
        }

        var reference = await avatars.SaveAsync(
            $"chat-{admin.Value.Id}", command.ContentType, command.Bytes, ct).ConfigureAwait(false);

        if (admin.Value.AvatarObjectKey is not null && admin.Value.AvatarObjectKey != reference.ObjectKey)
        {
            await avatars.DeleteAsync(admin.Value.AvatarObjectKey, ct).ConfigureAwait(false);
        }

        var url = $"/api/v1/chats/{admin.Value.Id}/avatar?v={Uri.EscapeDataString(reference.ObjectKey)}";
        admin.Value.SetAvatar(reference.ObjectKey, url, clock.UtcNow);
        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> RemoveChatAvatarAsync(
        RemoveChatAvatarCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new RemoveChatAvatarValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
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
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<(byte[] Bytes, string ContentType)>> GetChatAvatarAsync(
        GetChatAvatarQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetChatAvatarValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<(byte[] Bytes, string ContentType)>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(query.ChatId, ct).ConfigureAwait(false);
        // DMs and private chats: members only. Public channels: anyone (avatar is public).
        if (chat is null || chat.IsDeleted)
        {
            return Result<(byte[] Bytes, string ContentType)>.Fail(ChatErrors.NotFound);
        }

        var isPublicChannel = chat.Type == ChatType.Channel && chat.IsPublic;
        if (!isPublicChannel && !chat.IsMember(caller.UserId))
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

    /// <summary>
    /// Records an English membership system message in history. The in-memory
    /// chat projection is updated; persistence happens with the caller's own
    /// <c>UpdateAsync</c> so membership + message commit in a single write.
    /// <paramref name="actorId"/> resets that member's read watermark (everyone
    /// else gets an unread bump, like regular messages).
    /// </summary>
    private async Task<Result<MessageDto>> AppendSystemMessageAsync(
        Chat chat, string body, string actorId, CancellationToken ct)
    {
        var now = clock.UtcNow;
        var seq = await chats.AllocateSeqAsync(chat.Id, now, ct).ConfigureAwait(false);
        var created = Message.Create(
            ids.NewId(), chat.Id, seq, $"sys-{ids.NewId()}", null,
            MessageKind.System, body, null, null, now);
        if (created.IsFailure)
        {
            return Result<MessageDto>.Fail(created.Error);
        }

        try
        {
            await messages.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateMessageException)
        {
            return Result<MessageDto>.Fail(MessageErrors.ClientIdConflict);
        }

        chat.ApplyNewMessage(seq, created.Value!.Id, Message.PreviewOf(body), actorId, now);
        return Result<MessageDto>.Ok(
            await MessageQueries.EnrichAsync(messages, files, users, created.Value, ct, actorId)
                .ConfigureAwait(false));
    }
}
