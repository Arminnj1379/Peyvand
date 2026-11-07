using Messenger.Application.Features.Chats;
using Messenger.Application.Messaging;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>
/// Group collaboration: members, invites, subscriptions, and group/channel avatars.
/// </summary>
public interface IGroupService
{
    Task<Result<MembershipChangeResult>> AddMemberAsync(AddMemberCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<MembershipChangeResult>> RemoveMemberAsync(RemoveMemberCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<MembershipChangeResult>> LeaveChatAsync(LeaveChatCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> SetMemberRoleAsync(SetMemberRoleCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<string?>> GetInviteAsync(GetInviteQuery query, CallerContext caller, CancellationToken ct);

    Task<Result<string>> RotateInviteAsync(RotateInviteCommand command, CallerContext caller, CancellationToken ct);

    Task<Result> RevokeInviteAsync(RevokeInviteCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<MembershipChangeResult>> JoinViaInviteAsync(JoinViaInviteCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> SubscribeChannelAsync(SubscribeChannelCommand command, CallerContext caller, CancellationToken ct);

    Task<Result> UnsubscribeChannelAsync(UnsubscribeChannelCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<PublicChannelDto>> GetPublicChannelAsync(GetPublicChannelQuery query, CancellationToken ct);

    Task<Result<ChatDto>> UploadChatAvatarAsync(
        UploadChatAvatarCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<ChatDto>> RemoveChatAvatarAsync(
        RemoveChatAvatarCommand command, CallerContext caller, CancellationToken ct);

    Task<Result<(byte[] Bytes, string ContentType)>> GetChatAvatarAsync(
        GetChatAvatarQuery query, CallerContext caller, CancellationToken ct);
}
