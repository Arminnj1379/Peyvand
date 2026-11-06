using MediatR;
using Messenger.Application.Features.Notifications;

namespace Messenger.Api.Endpoints;

/// <summary>
/// Notification inbox (derived from read watermarks) plus conversation search.
/// No separate notification store: the database stays the source of truth.
/// </summary>
public static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications").RequireAuthorization();

        group.MapGet("/", async (int limit, ISender sender, CancellationToken ct) =>
            ResultMapper.ToHttp(
                await sender.Send(new ListNotificationsQuery(limit == 0 ? 20 : limit), ct)
                    .ConfigureAwait(false)))
        .WithName("ListNotifications")
        .WithSummary("Inbox rows for conversations with unread messages, most recent first.")
        .Produces<IReadOnlyList<NotificationItemDto>>()
        .ProducesProblem(400);

        return app;
    }
}
