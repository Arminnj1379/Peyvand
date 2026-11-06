using System.Security.Claims;
using Messenger.Api.Extensions;
using Messenger.Api.Hubs;
using Messenger.Application.Features.Chats;
using Messenger.Application.Messaging;
using Messenger.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Controllers;

public sealed record CreateDmRequest(string OtherUserId);

public sealed record CreateGroupRequest(string Title, IReadOnlyList<string> MemberIds, string? Description);

public sealed record CreateChannelRequest(
    string Title, string? Description, IReadOnlyList<string>? SubscriberIds,
    bool IsPublic, string? Username);

public sealed record AddMemberRequest(string UserId);

public sealed record SetRoleRequest(string Role);

public sealed record PinRequest(string MessageId);

public sealed record MarkReadRequest(long Seq);

public sealed record UpdateGroupRequest(string? Title, string? Description, bool? MembersCanInvite);

public sealed record SetVisibilityRequest(bool IsPublic, string? Username);

public sealed record JoinViaInviteRequest(string Code);

public sealed record MarkUnreadRequest(long Seq);

public sealed record SetPreferenceRequest(bool Value);

/// <summary>Conversations: DM/group/channel lifecycle, participants, pins, read watermarks.</summary>
[ApiController]
[Route("api/v1/chats")]
[Authorize]
public sealed class ChatsController(
    IConversationService conversations,
    IGroupService groups,
    IChatInteractionService interaction,
    IHubContext<MessengerHub> hub) : ControllerBase
{
    /// <summary>
    /// Notifies members' personal channels that a conversation appeared or
    /// changed membership. Chat-group fan-out cannot reach them: their open
    /// connections predate the chat group (new chat) or never joined it
    /// (added member).
    /// </summary>
    private Task NotifyMembersAsync(IEnumerable<string> memberIds, string chatId, CancellationToken ct)
    {
        var recipients = memberIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Select(MessengerHub.UserGroup)
            .ToList();
        return recipients.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Groups(recipients).SendAsync("chat:updated", new { chatId }, ct);
    }

    private Task NotifyMembersAsync(ChatDto chat, CancellationToken ct) =>
        NotifyMembersAsync(chat.Members.Select(m => m.UserId), chat.Id, ct);

    private Task NotifyChatAsync(string id, CancellationToken ct) =>
        hub.Clients.Group(MessengerHub.ChatGroup(id))
            .SendAsync("chat:updated", new { chatId = id }, ct);

    [HttpPost("dm")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> CreateDm([FromBody] CreateDmRequest req, CancellationToken ct)
    {
        var result = await conversations.CreateDmAsync(
            new CreateDmCommand(req.OtherUserId), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyMembersAsync(result.Value!, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("groups")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateGroup([FromBody] CreateGroupRequest req, CancellationToken ct)
    {
        var result = await conversations.CreateGroupAsync(
            new CreateGroupCommand(req.Title, req.MemberIds, req.Description), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyMembersAsync(result.Value!, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result, 201);
    }

    [HttpPost("channels")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateChannel([FromBody] CreateChannelRequest req, CancellationToken ct)
    {
        var result = await conversations.CreateChannelAsync(new CreateChannelCommand(
            req.Title, req.Description, req.SubscriberIds, req.IsPublic, req.Username), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyMembersAsync(result.Value!, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result, 201);
    }

    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatDto>), StatusCodes.Status200OK)]
    public async Task<IResult> ListMyChats([FromQuery] int limit = 0, CancellationToken ct = default) =>
        ResultMapper.ToHttp(await conversations.ListMyChatsAsync(
            new ListMyChatsQuery(limit == 0 ? 50 : limit), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> SearchMyChats(
        [FromQuery] string q, [FromQuery] int limit = 0, CancellationToken ct = default) =>
        ResultMapper.ToHttp(await conversations.SearchMyChatsAsync(
            new SearchMyChatsQuery(q, limit == 0 ? 20 : limit), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetChat([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await conversations.GetChatAsync(new GetChatQuery(id), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpPost("{id}/members")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> AddMember(
        [FromRoute] string id, [FromBody] AddMemberRequest req, CancellationToken ct)
    {
        var result = await groups.AddMemberAsync(new AddMemberCommand(id, req.UserId), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
            // The added member's open connections are not in the chat group yet.
            await NotifyMembersAsync([req.UserId], id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpDelete("{id}/members/{userId}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> RemoveMember(
        [FromRoute] string id, [FromRoute] string userId, CancellationToken ct)
    {
        var result = await groups.RemoveMemberAsync(
            new RemoveMemberCommand(id, userId), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
            // The removed member's connections stay in the chat group until
            // they resubscribe; tell them directly so the stale chat drops live.
            await NotifyMembersAsync([userId], id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/leave")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> LeaveChat([FromRoute] string id, CancellationToken ct)
    {
        var result = await groups.LeaveChatAsync(new LeaveChatCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteGroup([FromRoute] string id, CancellationToken ct)
    {
        var result = await conversations.DeleteGroupAsync(new DeleteGroupCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            // Members' open connections may still sit in the chat group;
            // tell them directly so the deleted chat drops live everywhere.
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
            await NotifyMembersAsync(result.Value!, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPut("{id}/members/{userId}/role")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> SetMemberRole(
        [FromRoute] string id, [FromRoute] string userId, [FromBody] SetRoleRequest req, CancellationToken ct)
    {
        var result = await groups.SetMemberRoleAsync(
            new SetMemberRoleCommand(id, userId, req.Role), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/pins")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> PinMessage(
        [FromRoute] string id, [FromBody] PinRequest req, CancellationToken ct)
    {
        var result = await interaction.PinMessageAsync(
            new PinMessageCommand(id, req.MessageId), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpDelete("{id}/pins/{messageId}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> UnpinMessage(
        [FromRoute] string id, [FromRoute] string messageId, CancellationToken ct)
    {
        var result = await interaction.UnpinMessageAsync(
            new UnpinMessageCommand(id, messageId), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/read")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> MarkRead(
        [FromRoute] string id, [FromBody] MarkReadRequest req, CancellationToken ct)
    {
        var result = await interaction.MarkReadAsync(
            new MarkReadCommand(id, req.Seq), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
            await hub.Clients.Group(MessengerHub.ChatGroup(id))
                .SendAsync("message:read", new { chatId = id, userId, seq = req.Seq }, ct)
                .ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateChatProfile(
        [FromRoute] string id, [FromBody] UpdateGroupRequest req, CancellationToken ct)
    {
        var result = await conversations.UpdateGroupAsync(new UpdateGroupCommand(
            id, req.Title, req.Description, req.MembersCanInvite), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsFailure && result.Error.Code == "chat.invalid_role")
        {
            // Same route edits channels: fall through to the channel profile.
            var channel = await conversations.UpdateChannelAsync(
                new UpdateChannelCommand(id, req.Title, req.Description), User.ToCaller(), ct)
                .ConfigureAwait(false);
            if (channel.IsSuccess)
            {
                await NotifyChatAsync(id, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(channel);
        }

        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPut("{id}/visibility")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> SetChannelVisibility(
        [FromRoute] string id, [FromBody] SetVisibilityRequest req, CancellationToken ct)
    {
        var result = await conversations.SetChannelVisibilityAsync(
            new SetChannelVisibilityCommand(id, req.IsPublic, req.Username), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpGet("{id}/invite")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetInvite([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await groups.GetInviteAsync(new GetInviteQuery(id), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpPost("{id}/invite/rotate")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> RotateInvite([FromRoute] string id, CancellationToken ct)
    {
        var result = await groups.RotateInviteAsync(new RotateInviteCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpDelete("{id}/invite")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> RevokeInvite([FromRoute] string id, CancellationToken ct)
    {
        var result = await groups.RevokeInviteAsync(new RevokeInviteCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("join")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> JoinViaInvite([FromBody] JoinViaInviteRequest req, CancellationToken ct)
    {
        var result = await groups.JoinViaInviteAsync(
            new JoinViaInviteCommand(req.Code), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            // Existing members' details panels show a stale roster otherwise.
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.Id))
                .SendAsync("chat:updated", new { chatId = result.Value.Id }, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result, 200);
    }

    [HttpPost("{id}/subscribe")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> SubscribeChannel([FromRoute] string id, CancellationToken ct)
    {
        var result = await groups.SubscribeChannelAsync(
            new SubscribeChannelCommand(id), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/unsubscribe")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> UnsubscribeChannel([FromRoute] string id, CancellationToken ct)
    {
        var result = await groups.UnsubscribeChannelAsync(
            new UnsubscribeChannelCommand(id), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await NotifyChatAsync(id, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpGet("public/{username}")]
    [ProducesResponseType(typeof(PublicChannelDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetPublicChannel([FromRoute] string username, CancellationToken ct) =>
        ResultMapper.ToHttp(await groups.GetPublicChannelAsync(new GetPublicChannelQuery(username), ct)
            .ConfigureAwait(false));

    [HttpPost("{id}/mute")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> SetMute(
        [FromRoute] string id, [FromBody] SetPreferenceRequest req, CancellationToken ct) =>
        ResultMapper.ToHttp(await interaction.SetMuteAsync(
            new SetMuteCommand(id, req.Value), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPost("{id}/archive")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> SetArchive(
        [FromRoute] string id, [FromBody] SetPreferenceRequest req, CancellationToken ct) =>
        ResultMapper.ToHttp(await interaction.SetArchiveAsync(
            new SetArchiveCommand(id, req.Value), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPost("{id}/unread")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> MarkUnread(
        [FromRoute] string id, [FromBody] MarkUnreadRequest req, CancellationToken ct) =>
        ResultMapper.ToHttp(await interaction.MarkUnreadAsync(
            new MarkUnreadCommand(id, req.Seq), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPost("{id}/avatar")]
    [IgnoreAntiforgeryToken]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> UploadChatAvatar([FromRoute] string id, IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
        {
            return Results.Problem(title: "No file uploaded.", statusCode: 400);
        }

        if (file.Length > UploadChatAvatarCommand.MaxBytes)
        {
            return ResultMapper.Problem(Domain.Chats.ChatErrors.InvalidAvatar);
        }

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct).ConfigureAwait(false);
        var result = await groups.UploadChatAvatarAsync(
            new UploadChatAvatarCommand(id, stream.ToArray(), file.ContentType), User.ToCaller(), ct)
            .ConfigureAwait(false);
        return ResultMapper.ToHttp(result);
    }

    [HttpDelete("{id}/avatar")]
    [ProducesResponseType(typeof(ChatDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> RemoveChatAvatar([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await groups.RemoveChatAvatarAsync(
            new RemoveChatAvatarCommand(id), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("{id}/avatar")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetChatAvatar([FromRoute] string id, CancellationToken ct)
    {
        var result = await groups.GetChatAvatarAsync(new GetChatAvatarQuery(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Bytes(result.Value!.Bytes, result.Value.ContentType)
            : ResultMapper.Problem(result.Error);
    }
}
