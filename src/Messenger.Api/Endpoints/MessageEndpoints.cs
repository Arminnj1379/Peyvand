using MediatR;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Endpoints;

public sealed record SendMessageRequest(string Body, string ClientMessageId, string? ReplyToMessageId, string? FileId);

public sealed record EditMessageRequest(string Body);

public sealed record ForwardMessageRequest(
    string SourceChatId, string SourceMessageId, string TargetChatId, string ClientMessageId);

public sealed record ToggleReactionRequest(string Emoji);

public sealed record BulkDeleteRequest(IReadOnlyList<string> MessageIds);

/// <summary>
/// Messages: send/history/sync/edit/delete/forward. Persist-then-broadcast:
/// hub fan-out happens only after the handler commits to MongoDB, so the
/// database stays the source of truth and reconnects re-sync via REST.
/// </summary>
public static class MessageEndpoints
{
    public static IEndpointRouteBuilder MapMessageEndpoints(this IEndpointRouteBuilder app)
    {
        var chats = app.MapGroup("/api/v1/chats").WithTags("Messages").RequireAuthorization();

        chats.MapPost("/{chatId}/messages", async (string chatId, SendMessageRequest req,
            ISender sender, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(
                new SendMessageCommand(chatId, req.Body, req.ClientMessageId, req.ReplyToMessageId, req.FileId), ct)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(chatId))
                    .SendAsync("message:new", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("SendMessage")
        .WithSummary("Sends a text or attachment message (idempotent on clientMessageId).")
        .Produces<MessageDto>(201)
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        chats.MapGet("/{chatId}/messages", async (string chatId, long? beforeSeq, int limit,
            ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new GetHistoryQuery(chatId, beforeSeq, limit == 0 ? 30 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("GetHistory")
        .WithSummary("Cursor-based history (beforeSeq exclusive, newest page first).")
        .Produces<HistoryPageDto>()
        .ProducesProblem(400)
        .ProducesProblem(403);

        chats.MapGet("/{chatId}/messages/sync", async (string chatId, long afterSeq, int limit,
            ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new SyncMessagesQuery(chatId, afterSeq, limit == 0 ? 100 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("SyncMessages")
        .WithSummary("Gap-fill for reconnects (messages after a known seq).")
        .Produces<IReadOnlyList<MessageDto>>()
        .ProducesProblem(400)
        .ProducesProblem(403);

        chats.MapGet("/{chatId}/pins", async (string chatId, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new ListPinnedQuery(chatId), ct).ConfigureAwait(false)))
        .WithName("ListPinned")
        .Produces<IReadOnlyList<MessageDto>>()
        .ProducesProblem(403);

        var messages = app.MapGroup("/api/v1/messages").WithTags("Messages").RequireAuthorization();

        messages.MapGet("/search", async (string q, int limit, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new SearchMessagesQuery(q, limit == 0 ? 20 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("SearchMessages")
        .WithSummary("Full-text search across the caller's conversations, newest first.")
        .Produces<IReadOnlyList<MessageDto>>()
        .ProducesProblem(400);

        messages.MapPatch("/{id}", async (string id, EditMessageRequest req,
            ISender sender, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new EditMessageCommand(id, req.Body), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.ChatId))
                    .SendAsync("message:edited", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("EditMessage")
        .Produces<MessageDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        messages.MapDelete("/{id}", async (string id,
            ISender sender, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new DeleteMessageCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.ChatId))
                    .SendAsync("message:deleted", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("DeleteMessage")
        .Produces<MessageDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        messages.MapPost("/forward", async (ForwardMessageRequest req,
            ISender sender, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new ForwardMessageCommand(
                req.SourceChatId, req.SourceMessageId, req.TargetChatId, req.ClientMessageId), ct)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(req.TargetChatId))
                    .SendAsync("message:new", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("ForwardMessage")
        .Produces<MessageDto>(201)
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        messages.MapPost("/{id}/reactions", async (string id, ToggleReactionRequest req,
            ISender sender, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new ToggleReactionCommand(id, req.Emoji), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.ChatId))
                    .SendAsync("message:reacted", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("ToggleReaction")
        .WithSummary("Toggles the caller's emoji reaction (members incl. subscribers; not on deleted).")
        .Produces<MessageDto>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        messages.MapGet("/{id}/replies", async (string id, int limit,
            ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new GetRepliesQuery(id, limit == 0 ? 30 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("GetReplies")
        .WithSummary("Reply thread: direct children of a message, oldest-first.")
        .Produces<IReadOnlyList<MessageDto>>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        messages.MapPost("/bulk-delete", async (BulkDeleteRequest req,
            ISender sender, Microsoft.AspNetCore.SignalR.IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new BulkDeleteMessagesCommand(req.MessageIds), ct)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                // Same per-message event as single deletes: clients reconcile one path.
                foreach (var group in result.Value!.GroupBy(m => m.ChatId))
                {
                    foreach (var m in group)
                    {
                        await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(group.Key))
                            .SendAsync("message:deleted", m, ct).ConfigureAwait(false);
                    }
                }
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("BulkDeleteMessages")
        .WithSummary("Deletes up to 50 selected messages (own always; others need admin).")
        .Produces<IReadOnlyList<MessageDto>>()
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(404);

        return app;
    }
}
