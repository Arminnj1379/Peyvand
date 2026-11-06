using FluentValidation;

namespace Messenger.Application.Features.Notifications;

/// <summary>One inbox row: a conversation with unread messages for the caller.</summary>
public sealed record NotificationItemDto(
    string ChatId,
    string ChatType,
    string Title,
    string? Preview,
    int UnreadCount,
    bool IsMuted,
    DateTime? At);

/// <summary>
/// Notification inbox derived from read watermarks (no separate store to
/// drift). Muted chats are included but flagged so clients can de-emphasize.
/// </summary>
public sealed record ListNotificationsQuery(int Limit);

public sealed class ListNotificationsValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}
