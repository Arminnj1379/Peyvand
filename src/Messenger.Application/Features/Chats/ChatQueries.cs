using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;

namespace Messenger.Application.Features.Chats;

/// <summary>Shared enrichment: member display names for chat projections.</summary>
internal static class ChatQueries
{
    public static async Task<ChatDto> EnrichAsync(
        IChatStore chats,
        IUserStore users,
        Chat chat,
        string callerId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(chat);
        var map = await UserMapAsync(users, chat.Members.Select(m => m.UserId), ct).ConfigureAwait(false);
        return MessagingMapping.ToDto(chat, map, callerId);
    }

    public static async Task<Dictionary<string, (string Username, string DisplayName)>> UserMapAsync(
        IUserStore users,
        IEnumerable<string> userIds,
        CancellationToken ct)
    {
        // One round-trip for the whole batch (was: sequential GetById per id).
        // Missing entries (deleted users, malformed ids) project as unknown.
        var distinct = userIds.Distinct().ToList();
        var found = await users.ListByIdsAsync(distinct, ct).ConfigureAwait(false);
        var map = new Dictionary<string, (string Username, string DisplayName)>();
        foreach (var user in found)
        {
            map[user.Id] = (user.Username, user.DisplayName);
        }

        foreach (var id in distinct)
        {
            map.TryAdd(id, ("unknown", "Unknown"));
        }

        return map;
    }
}

public sealed record ListMyChatsQuery(int Limit);

public sealed class ListMyChatsValidator : AbstractValidator<ListMyChatsQuery>
{
    public ListMyChatsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed record GetChatQuery(string ChatId);

public sealed class GetChatValidator : AbstractValidator<GetChatQuery>
{
    public GetChatValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}
