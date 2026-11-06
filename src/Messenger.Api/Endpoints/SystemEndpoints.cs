using MediatR;
using Messenger.Application.Features.System;

namespace Messenger.Api.Endpoints;

/// <summary>Foundation endpoints. Feature endpoints (auth/chats/messages/...) land per roadmap phase.</summary>
public static class SystemEndpoints
{
    public static IEndpointRouteBuilder MapSystemEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/system").WithTags("System");

        group.MapGet("/info", async (string? locale, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new GetSystemInfoQuery(locale), ct).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : Results.Problem(title: result.Error.Message, statusCode: 500);
        })
        .WithName("GetSystemInfo")
        .WithSummary("Service info + server time. Proves API -> Application wiring.")
        .Produces<SystemInfoDto>()
        .ProducesProblem(500);

        return app;
    }
}
