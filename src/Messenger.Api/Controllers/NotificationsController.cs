using Messenger.Api.Extensions;
using Messenger.Application.Features.Notifications;
using Messenger.Application.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Messenger.Api.Controllers;

/// <summary>
/// Notification inbox (derived from read watermarks) plus conversation search.
/// No separate notification store: the database stays the source of truth.
/// </summary>
[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController(INotificationService notifications) : ControllerBase
{
    [HttpGet("")]
    [ProducesResponseType(typeof(IReadOnlyList<NotificationItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> ListNotifications([FromQuery] int limit = 0, CancellationToken ct = default) =>
        ResultMapper.ToHttp(await notifications.ListNotificationsAsync(
            new ListNotificationsQuery(limit == 0 ? 20 : limit), User.ToCaller(), ct).ConfigureAwait(false));
}
