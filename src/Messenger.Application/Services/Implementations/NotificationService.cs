using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Features.Chats;
using Messenger.Application.Features.Notifications;
using Messenger.Application.Services.Interfaces;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Implementations;

/// <summary>Unread inbox. Logic moved verbatim from the former MediatR handler.</summary>
public sealed class NotificationService(
    IChatStore chats,
    IUserStore users) : INotificationService
{
    public async Task<Result<IReadOnlyList<NotificationItemDto>>> ListNotificationsAsync(
        ListNotificationsQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new ListNotificationsValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<NotificationItemDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(caller.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<IReadOnlyList<NotificationItemDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var mine = await chats.ListForUserAsync(caller.UserId, 200, ct).ConfigureAwait(false);
        var unread = mine
            .Where(c => !c.IsDeleted && (c.GetMember(caller.UserId)?.UnreadCount ?? 0) > 0)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .Take(query.Limit)
            .ToList();

        var peerIds = unread
            .Where(c => c.Type == ChatType.Dm)
            .Select(c => c.Members.FirstOrDefault(m => m.UserId != caller.UserId)?.UserId)
            .Where(id => id is not null)
            .Select(id => id!)
            .ToList();
        var names = await ChatQueries.UserMapAsync(users, peerIds, ct).ConfigureAwait(false);

        // Master switch off: clients show the paused state from settings, but
        // the inbox stays queryable (no data loss, no surprise clears).
        return Result<IReadOnlyList<NotificationItemDto>>.Ok(unread.Select(c =>
        {
            var self = c.GetMember(caller.UserId);
            string title = c.Type == ChatType.Dm
                ? c.Members.FirstOrDefault(m => m.UserId != caller.UserId) is { } peer &&
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
