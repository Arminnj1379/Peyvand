using MediatR;
using Messenger.Application.Features.Chats;
using Messenger.Application.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Endpoints;

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

public sealed record UpdateChannelRequest(string? Title, string? Description);

public sealed record SetVisibilityRequest(bool IsPublic, string? Username);

public sealed record JoinViaInviteRequest(string Code);

public sealed record MarkUnreadRequest(long Seq);

public sealed record SetPreferenceRequest(bool Value);

/// <summary>Conversations: DM/group/channel lifecycle, participants, pins, read watermarks.</summary>
public static class ChatEndpoints
{
    /// <summary>
    /// Notifies members' personal channels that a conversation appeared or
    /// changed membership. Chat-group fan-out cannot reach them: their open
    /// connections predate the chat group (new chat) or never joined it
    /// (added member). Clients already handle <c>chat:updated</c> by
    /// refetching the list and subscribing, so this is the whole live path.
    /// Payload carries only the id — no content, members-only recipients.
    /// </summary>
    private static Task NotifyMembersAsync(
        Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub,
        IEnumerable<string> memberIds,
        string chatId,
        CancellationToken ct)
    {
        var groups = memberIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .Select(Hubs.MessengerHub.UserGroup)
            .ToList();
        return groups.Count == 0
            ? Task.CompletedTask
            : hub.Clients.Groups(groups).SendAsync("chat:updated", new { chatId }, ct);
    }

    private static Task NotifyMembersAsync(
        Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub,
        ChatDto chat,
        CancellationToken ct) =>
        NotifyMembersAsync(hub, chat.Members.Select(m => m.UserId), chat.Id, ct);

    public static IEndpointRouteBuilder MapChatEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/chats").WithTags("Chats").RequireAuthorization();

        group.MapPost("/dm", async (CreateDmRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateDmCommand(req.OtherUserId), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await NotifyMembersAsync(hub, result.Value!, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("CreateDm")
        .WithSummary("Direct conversation (idempotent: same pair returns the existing DM).")
        .Produces<ChatDto>()
        .ProducesProblem(400)
        .ProducesProblem(404);

        group.MapPost("/groups", async (CreateGroupRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateGroupCommand(req.Title, req.MemberIds, req.Description), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await NotifyMembersAsync(hub, result.Value!, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("CreateGroup")
        .Produces<ChatDto>(201)
        .ProducesProblem(400);

        group.MapPost("/channels", async (CreateChannelRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new CreateChannelCommand(req.Title, req.Description, req.SubscriberIds, req.IsPublic, req.Username), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await NotifyMembersAsync(hub, result.Value!, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("CreateChannel")
        .Produces<ChatDto>(201)
        .ProducesProblem(400);

        group.MapGet("/", async (int limit, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new ListMyChatsQuery(limit == 0 ? 50 : limit), ct).ConfigureAwait(false)))
        .WithName("ListMyChats")
        .WithSummary("Caller's conversations, most recent first.")
        .Produces<IReadOnlyList<ChatDto>>();

        group.MapGet("/search", async (string q, int limit, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new SearchMyChatsQuery(q, limit == 0 ? 20 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("SearchMyChats")
        .WithSummary("Searches the caller's conversations by title, channel username, or DM peer name.")
        .Produces<IReadOnlyList<ChatDto>>()
        .ProducesProblem(400);

        group.MapGet("/{id}", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetChatQuery(id), ct).ConfigureAwait(false)))
        .WithName("GetChat")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/members", async (string id, AddMemberRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new AddMemberCommand(id, req.UserId), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
                // The added member's open connections are not in the chat group yet.
                await NotifyMembersAsync(hub, [req.UserId], id, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("AddMember")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        group.MapDelete("/{id}/members/{userId}", async (string id, string userId, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new RemoveMemberCommand(id, userId), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
                // The removed member's connections stay in the chat group until
                // they resubscribe; tell them directly so the stale chat drops live.
                await NotifyMembersAsync(hub, [userId], id, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("RemoveMember")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/leave", async (string id, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new LeaveChatCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("LeaveChat")
        .Produces(200)
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapDelete("/{id}", async (string id, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteGroupCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // Members' open connections may still sit in the chat group;
                // tell them directly so the deleted chat drops live everywhere.
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
                await NotifyMembersAsync(hub, result.Value!, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("DeleteGroup")
        .WithSummary("Deletes a group/channel (owner-only; history is preserved, lists read it as not-found).")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPut("/{id}/members/{userId}/role", async (string id, string userId, SetRoleRequest req,
            ISender sender, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new SetMemberRoleCommand(id, userId, req.Role), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("SetMemberRole")
        .Produces<ChatDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/pins", async (string id, PinRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new PinMessageCommand(id, req.MessageId), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("PinMessage")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        group.MapDelete("/{id}/pins/{messageId}", async (string id, string messageId, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new UnpinMessageCommand(id, messageId), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("UnpinMessage")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/read", async (string id, MarkReadRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, HttpContext ctx, CancellationToken ct) =>
        {
            var result = await sender.Send(new MarkReadCommand(id, req.Seq), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                var userId = ctx.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                    ?? ctx.User.FindFirst("sub")?.Value;
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("message:read", new { chatId = id, userId, seq = req.Seq }, ct)
                    .ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("MarkRead")
        .WithSummary("Advances the caller's read watermark (read receipts).")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPatch("/{id}", async (string id, UpdateGroupRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new UpdateGroupCommand(id, req.Title, req.Description, req.MembersCanInvite), ct)
                .ConfigureAwait(false);
            if (result.IsFailure && result.Error.Code == "chat.invalid_role")
            {
                // Same route edits channels: fall through to the channel profile.
                var channel = await sender.Send(new UpdateChannelCommand(id, req.Title, req.Description), ct)
                    .ConfigureAwait(false);
                if (channel.IsSuccess)
                {
                    await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                        .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
                }

                return ResultMapper.ToHttp(channel);
            }

            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("UpdateChatProfile")
        .WithSummary("Edits group/channel title, description, and (groups) member-invite policy. Admin-only.")
        .Produces<ChatDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPut("/{id}/visibility", async (string id, SetVisibilityRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SetChannelVisibilityCommand(id, req.IsPublic, req.Username), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("SetChannelVisibility")
        .WithSummary("Switches a channel between public (username) and private. Admin-only.")
        .Produces<ChatDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        group.MapGet("/{id}/invite", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetInviteQuery(id), ct).ConfigureAwait(false)))
        .WithName("GetInvite")
        .WithSummary("Returns the active invite code (any member; null when none).")
        .Produces<string?>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/invite/rotate", async (string id, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new RotateInviteCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("RotateInvite")
        .WithSummary("Creates (or replaces) the invite link. Admin-only.")
        .Produces<string>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapDelete("/{id}/invite", async (string id, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new RevokeInviteCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("RevokeInvite")
        .WithSummary("Revokes the invite link. Admin-only.")
        .Produces(200)
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/join", async (JoinViaInviteRequest req, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new JoinViaInviteCommand(req.Code), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // Existing members' details panels show a stale roster otherwise.
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.Id))
                    .SendAsync("chat:updated", new { chatId = result.Value.Id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result, 200);
        })
        .WithName("JoinViaInvite")
        .WithSummary("Joins a group/channel with an invite code (already-members get the chat).")
        .Produces<ChatDto>()
        .ProducesProblem(400)
        .ProducesProblem(404)
        .ProducesProblem(409);

        group.MapPost("/{id}/subscribe", async (string id, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new SubscribeChannelCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("SubscribeChannel")
        .WithSummary("Subscribes to a public channel (private channels need an invite).")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        group.MapPost("/{id}/unsubscribe", async (string id, ISender sender,
            Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new UnsubscribeChannelCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(id))
                    .SendAsync("chat:updated", new { chatId = id }, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("UnsubscribeChannel")
        .WithSummary("Leaves a channel subscription.")
        .Produces(200)
        .ProducesProblem(404);

        group.MapGet("/public/{username}", async (string username, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetPublicChannelQuery(username), ct).ConfigureAwait(false)))
        .WithName("GetPublicChannel")
        .WithSummary("Resolves a public channel card by username (no membership required).")
        .Produces<PublicChannelDto>()
        .ProducesProblem(404);

        group.MapPost("/{id}/mute", async (string id, SetPreferenceRequest req, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new SetMuteCommand(id, req.Value), ct).ConfigureAwait(false)))
        .WithName("SetMute")
        .WithSummary("Mutes/unmutes the conversation for the caller (caller-relative, no admin needed).")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/archive", async (string id, SetPreferenceRequest req, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new SetArchiveCommand(id, req.Value), ct).ConfigureAwait(false)))
        .WithName("SetArchive")
        .WithSummary("Archives/unarchives the conversation for the caller.")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/unread", async (string id, MarkUnreadRequest req, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new MarkUnreadCommand(id, req.Seq), ct).ConfigureAwait(false)))
        .WithName("MarkUnread")
        .WithSummary("Marks the conversation unread from a message seq onward (caller-relative).")
        .Produces<ChatDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapPost("/{id}/avatar", async (string id, IFormFile file, ISender sender, CancellationToken ct) =>
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
            var result = await sender.Send(
                new UploadChatAvatarCommand(id, stream.ToArray(), file.ContentType), ct).ConfigureAwait(false);
            return ResultMapper.ToHttp(result);
        })
        .DisableAntiforgery()
        .WithName("UploadChatAvatar")
        .WithSummary("Uploads the group/channel avatar (JPEG/PNG/WebP, max 2 MB). Admin-only.")
        .Produces<ChatDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapDelete("/{id}/avatar", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new RemoveChatAvatarCommand(id), ct).ConfigureAwait(false)))
        .WithName("RemoveChatAvatar")
        .Produces<ChatDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        group.MapGet("/{id}/avatar", async (string id, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetChatAvatarQuery(id), ct).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Bytes(result.Value!.Bytes, result.Value.ContentType)
                : ResultMapper.Problem(result.Error);
        })
        .WithName("GetChatAvatar")
        .WithSummary("Serves the group/channel avatar bytes (members; public channels allow anyone).")
        .Produces(200, contentType: "image/jpeg")
        .ProducesProblem(403)
        .ProducesProblem(404);

        return app;
    }
}
