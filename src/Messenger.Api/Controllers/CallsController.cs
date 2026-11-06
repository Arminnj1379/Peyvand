using Messenger.Api.Extensions;
using Messenger.Api.Hubs;
using Messenger.Application.Calls;
using Messenger.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Controllers;

public sealed record StartCallRequest(string MediaType);

/// <summary>
/// Call signaling. The backend owns authorization + lifecycle and mints
/// short-lived LiveKit tokens; media flows client↔SFU and never traverses
/// the API. Every mutation fans out to the chat group after the commit
/// (persist-then-broadcast, same as messages).
/// </summary>
[ApiController]
[Route("api/v1/calls")]
[Authorize]
public sealed class CallsController(
    ICallService calls,
    IHubContext<MessengerHub> hub) : ControllerBase
{
    [HttpPost("/api/v1/chats/{chatId}/calls")]
    [ProducesResponseType(typeof(CallDto), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> StartCall(
        [FromRoute] string chatId, [FromBody] StartCallRequest req, CancellationToken ct)
    {
        var result = await calls.StartCallAsync(
            new StartCallCommand(chatId, req.MediaType), User.ToCaller(), ct).ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(chatId))
                .SendAsync("call:incoming", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result, 201);
    }

    [HttpGet("/api/v1/chats/{chatId}/calls")]
    [ProducesResponseType(typeof(IReadOnlyList<CallDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> ListCalls(
        [FromRoute] string chatId, [FromQuery] int limit = 0, CancellationToken ct = default) =>
        ResultMapper.ToHttp(await calls.ListCallsAsync(
            new ListCallsQuery(chatId, limit == 0 ? 20 : limit), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(CallDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetCall([FromRoute] string id, CancellationToken ct) =>
        ResultMapper.ToHttp(await calls.GetCallAsync(new GetCallQuery(id), User.ToCaller(), ct)
            .ConfigureAwait(false));

    [HttpPost("{id}/accept")]
    [ProducesResponseType(typeof(CallDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> AcceptCall([FromRoute] string id, CancellationToken ct)
    {
        var result = await calls.AcceptCallAsync(new AcceptCallCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.ChatId))
                .SendAsync("call:accepted", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/reject")]
    [ProducesResponseType(typeof(CallDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> RejectCall([FromRoute] string id, CancellationToken ct)
    {
        var result = await calls.RejectCallAsync(new RejectCallCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.ChatId))
                .SendAsync("call:rejected", result.Value, ct).ConfigureAwait(false);
            if (result.Value.Status == "Ended")
            {
                await hub.Clients.Group(MessengerHub.ChatGroup(result.Value.ChatId))
                    .SendAsync("call:ended", result.Value, ct).ConfigureAwait(false);
            }
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/join")]
    [ProducesResponseType(typeof(CallJoinDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> JoinCall([FromRoute] string id, CancellationToken ct)
    {
        var result = await calls.JoinCallAsync(new JoinCallCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.Call.ChatId))
                .SendAsync("call:joined", result.Value.Call, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/leave")]
    [ProducesResponseType(typeof(CallDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> LeaveCall([FromRoute] string id, CancellationToken ct)
    {
        var result = await calls.LeaveCallAsync(new LeaveCallCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.ChatId))
                .SendAsync("call:left", result.Value, ct).ConfigureAwait(false);
            if (result.Value.Status == "Ended")
            {
                await hub.Clients.Group(MessengerHub.ChatGroup(result.Value.ChatId))
                    .SendAsync("call:ended", result.Value, ct).ConfigureAwait(false);
            }
        }

        return ResultMapper.ToHttp(result);
    }

    [HttpPost("{id}/end")]
    [ProducesResponseType(typeof(CallDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> EndCall([FromRoute] string id, CancellationToken ct)
    {
        var result = await calls.EndCallAsync(new EndCallCommand(id), User.ToCaller(), ct)
            .ConfigureAwait(false);
        if (result.IsSuccess)
        {
            await hub.Clients.Group(MessengerHub.ChatGroup(result.Value!.ChatId))
                .SendAsync("call:ended", result.Value, ct).ConfigureAwait(false);
        }

        return ResultMapper.ToHttp(result);
    }
}
