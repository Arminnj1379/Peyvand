using MediatR;
using Messenger.Application.Calls;
using Microsoft.AspNetCore.SignalR;

namespace Messenger.Api.Endpoints;

public sealed record StartCallRequest(string MediaType);

/// <summary>
/// Call signaling. The backend owns authorization + lifecycle and mints
/// short-lived LiveKit tokens; media flows client↔SFU and never traverses
/// the API. Every mutation fans out to the chat group after the commit
/// (persist-then-broadcast, same as messages).
/// </summary>
public static class CallEndpoints
{
    public static IEndpointRouteBuilder MapCallEndpoints(this IEndpointRouteBuilder app)
    {
        var chats = app.MapGroup("/api/v1/chats").WithTags("Calls").RequireAuthorization();

        chats.MapPost("/{chatId}/calls", async (string chatId, StartCallRequest req,
            ISender sender, IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new StartCallCommand(chatId, req.MediaType), ct)
                .ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(chatId))
                    .SendAsync("call:incoming", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result, 201);
        })
        .WithName("StartCall")
        .WithSummary("Starts a voice/video call (one live call per conversation). Members only; channels need post rights.")
        .Produces<CallDto>(201)
        .ProducesProblem(400)
        .ProducesProblem(403)
        .ProducesProblem(409);

        chats.MapGet("/{chatId}/calls", async (string chatId, int limit,
            ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new ListCallsQuery(chatId, limit == 0 ? 20 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("ListCalls")
        .WithSummary("Call status and history for a conversation, newest first.")
        .Produces<IReadOnlyList<CallDto>>()
        .ProducesProblem(400)
        .ProducesProblem(403);

        var calls = app.MapGroup("/api/v1/calls").WithTags("Calls").RequireAuthorization();

        calls.MapGet("/{id}", async (string id, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(await sender.Send(new GetCallQuery(id), ct).ConfigureAwait(false)))
        .WithName("GetCall")
        .Produces<CallDto>()
        .ProducesProblem(403)
        .ProducesProblem(404);

        calls.MapPost("/{id}/accept", async (string id,
            ISender sender, IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new AcceptCallCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.ChatId))
                    .SendAsync("call:accepted", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("AcceptCall")
        .Produces<CallDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        calls.MapPost("/{id}/reject", async (string id,
            ISender sender, IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new RejectCallCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.ChatId))
                    .SendAsync("call:rejected", result.Value, ct).ConfigureAwait(false);
                if (result.Value.Status == "Ended")
                {
                    await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value.ChatId))
                        .SendAsync("call:ended", result.Value, ct).ConfigureAwait(false);
                }
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("RejectCall")
        .WithSummary("Declines a ringing call (ends 1-1 calls outright).")
        .Produces<CallDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        calls.MapPost("/{id}/join", async (string id,
            ISender sender, IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new JoinCallCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.Call.ChatId))
                    .SendAsync("call:joined", result.Value.Call, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("JoinCall")
        .WithSummary("Joins media: returns the LiveKit URL + short-lived token. First join activates the call.")
        .Produces<CallJoinDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409)
        .ProducesProblem(503);

        calls.MapPost("/{id}/leave", async (string id,
            ISender sender, IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new LeaveCallCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.ChatId))
                    .SendAsync("call:left", result.Value, ct).ConfigureAwait(false);
                if (result.Value.Status == "Ended")
                {
                    await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value.ChatId))
                        .SendAsync("call:ended", result.Value, ct).ConfigureAwait(false);
                }
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("LeaveCall")
        .WithSummary("Leaves media without ending the call for others (last one out ends it).")
        .Produces<CallDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        calls.MapPost("/{id}/end", async (string id,
            ISender sender, IHubContext<Hubs.MessengerHub> hub, CancellationToken ct) =>
        {
            var result = await sender.Send(new EndCallCommand(id), ct).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                await hub.Clients.Group(Hubs.MessengerHub.ChatGroup(result.Value!.ChatId))
                    .SendAsync("call:ended", result.Value, ct).ConfigureAwait(false);
            }

            return ResultMapper.ToHttp(result);
        })
        .WithName("EndCall")
        .WithSummary("Ends the call for everyone (DM: either party; group/channel: caller or admin).")
        .Produces<CallDto>()
        .ProducesProblem(403)
        .ProducesProblem(404)
        .ProducesProblem(409);

        return app;
    }
}
