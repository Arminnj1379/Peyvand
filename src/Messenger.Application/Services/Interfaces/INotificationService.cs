using Messenger.Application.Features.Notifications;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Interfaces;

/// <summary>Unread inbox derived from read watermarks (no separate store).</summary>
public interface INotificationService
{
    Task<Result<IReadOnlyList<NotificationItemDto>>> ListNotificationsAsync(
        ListNotificationsQuery query, CallerContext caller, CancellationToken ct);
}
