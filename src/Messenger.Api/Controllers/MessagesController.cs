using Messenger.Api.Extensions;
using Messenger.Api.Hubs;
using Messenger.Application.Features.Messages;
using Messenger.Application.Messaging;
using Messenger.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Controllers;

public sealed record SendMessageRequest(string Body, string ClientMessageId, string? ReplyToMessageId, string? FileId);

public sealed record EditMessageRequest(string Body);

public sealed record ForwardMessageRequest(
    string SourceChatId, string SourceMessageId, string TargetChatId, string ClientMessageId);

public sealed record ToggleReactionRequest(string Emoji);

public sealed record BulkDeleteRequest(IReadOnlyList<string> MessageIds);

/// <summary>
/// Messages: send/history/sync/edit/delete/forward. Persist-then-broadcast:
/// hub fan-out happens only after the service commits to MongoDB, so the
/// database stays the source of truth and reconnects re-sync via REST.
/// </summary>
[ApiController]
[Route("api/v1/messages")]
[Authorize]
public sealed class MessagesController(
    IMessageService messages,
    IHubContext<MessengerHub> hub) : ControllerBase
{
    [HttpPost("/api/v1/chats/{chatId}/messages")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> SendMessage(
        [FromRoute] string chatId, [FromBody] SendMessageRequest req, CancellationToken ct)
    {
        var result = await messages.SendMessageAsync(new SendMessageCommand(
            chatId, req.Body, req.ClientMessageId, req.ReplyToMessageId, req.FileId),
            User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(chatId))
                .SendAsync("message:new", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result, 201);
    }

    [HttpGet("/api/v1/chats/{chatId}/messages")]
    [ProducesResponseType(typeof(HistoryPageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> GetHistory(
        [FromRoute] string chatId,
        [FromQuery] long? beforeSeq,
        [FromQuery] int limit = 0,
        CancellationToken ct = default) =>
        ResultMapper.ToHttp(await messages.GetHistoryAsync(
            new GetHistoryQuery(chatId, beforeSeq, limit == 0 ? 30 : limit),
            User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("/api/v1/chats/{chatId}/messages/sync")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> SyncMessages(
        [FromRoute] string chatId,
        [FromQuery] long afterSeq,
        [FromQuery] int limit = 0,
        CancellationToken ct = default) =>
        ResultMapper.ToHttp(await messages.SyncMessagesAsync(
            new SyncMessagesQuery(chatId, afterSeq, limit == 0 ? 100 : limit),
            User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("/api/v1/chats/{chatId}/pins")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> ListPinned([FromRoute] string chatId, CancellationToken ct) =>
        ResultMapper.ToHttp(await messages.ListPinnedAsync(
            new ListPinnedQuery(chatId), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpGet("search")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> SearchMessages(
        [FromQuery] string q, [FromQuery] int limit = 0, CancellationToken ct = default) =>
        ResultMapper.ToHttp(await messages.SearchMessagesAsync(
            new SearchMessagesQuery(q, limit == 0 ? 20 : limit), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPatch("{id}")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> EditMessage(
        [FromRoute] string id, [FromBody] EditMessageRequest req, CancellationToken ct)
    {
        var result = await messages.EditMessageAsync(
            new EditMessageCommand(id, req.Body), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.ChatId))
                .SendAsync("message:edited", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DeleteMessage([FromRoute] string id, CancellationToken ct)
    {
        var result = await messages.DeleteMessageAsync(
            new DeleteMessageCommand(id), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.ChatId))
                .SendAsync("message:deleted", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("forward")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ForwardMessage([FromBody] ForwardMessageRequest req, CancellationToken ct)
    {
        var result = await messages.ForwardMessageAsync(new ForwardMessageCommand(
            req.SourceChatId, req.SourceMessageId, req.TargetChatId, req.ClientMessageId),
            User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(req.TargetChatId))
                .SendAsync("message:new", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result, 201);
    }

    [HttpPost("{id}/reactions")]
    [ProducesResponseType(typeof(MessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ToggleReaction(
        [FromRoute] string id, [FromBody] ToggleReactionRequest req, CancellationToken ct)
    {
        var result = await messages.ToggleReactionAsync(
            new ToggleReactionCommand(id, req.Emoji), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.ChatId))
                .SendAsync("message:reacted", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpGet("{id}/replies")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetReplies(
        [FromRoute] string id, [FromQuery] int limit = 0, CancellationToken ct = default) =>
        ResultMapper.ToHttp(await messages.GetRepliesAsync(
            new GetRepliesQuery(id, limit == 0 ? 30 : limit), User.ToCaller(), ct).ConfigureAwait(false));

    [HttpPost("bulk-delete")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> BulkDeleteMessages([FromBody] BulkDeleteRequest req, CancellationToken ct)
    {
        var result = await messages.BulkDeleteMessagesAsync(
            new BulkDeleteMessagesCommand(req.MessageIds), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            // Same per-message event as single deletes: clients reconcile one path.
            foreach (var group in result.Value!.GroupBy(m => m.ChatId))
            {
                foreach (var m in group)
                {
                    await hub.Clients.Group(MessengerHub.ChatGroup(group.Key))
                        .SendAsync("message:deleted", m, ct).ConfigureAwait(false);
                }
            }
        }

        return ResultMapper.ToHttp(result);
    }
}
