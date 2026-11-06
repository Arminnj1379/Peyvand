using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Chats;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

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
public sealed record ListNotificationsQuery(int Limit) : IRequest<Result<IReadOnlyList<NotificationItemDto>>>;

public sealed class ListNotificationsValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class ListNotificationsHandler(
    IChatStore chats,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<ListNotificationsQuery, Result<IReadOnlyList<NotificationItemDto>>>
{
    public async Task<Result<IReadOnlyList<NotificationItemDto>>> Handle(
        ListNotificationsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<NotificationItemDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<IReadOnlyList<NotificationItemDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var mine = await chats.ListForUserAsync(currentUser.UserId, 200, ct).ConfigureAwait(false);
        var unread = mine
            .Where(c => !c.IsDeleted && (c.GetMember(currentUser.UserId)?.UnreadCount ?? 0) > 0)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Take(request.Limit)
            .ToList();

        var peerIds = unread
            .Where(c => c.Type == ChatType.Dm)
            .Select(c => c.Members.FirstOrDefault(m => m.UserId != currentUser.UserId)?.UserId)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
        var names = await ChatQueries.UserMapAsync(users, peerIds, ct).ConfigureAwait(false);

        // Master switch off: clients show the paused state from settings, but
        // the inbox stays queryable (no data loss, no surprise clears).
        return Result<IReadOnlyList<NotificationItemDto>>.Ok(unread.Select(c =>
        {
            var self = c.GetMember(currentUser.UserId);
            string title = c.Type == ChatType.Dm
                ? c.Members.FirstOrDefault(m => m.UserId != currentUser.UserId) is { } peer &&
                    names.TryGetValue(peer.UserId, out var name)
                    ? name.DisplayName
                    : "Direct message"
                : c.Title ?? "Conversation";
            return new NotificationItemDto(
                c.Id, c.Type.ToString(), title, c.LastMessagePreview,
                self?.UnreadCount ?? 0, self?.IsMuted ?? false, c.LastMessageAt);
        }).ToList());
    }
}
