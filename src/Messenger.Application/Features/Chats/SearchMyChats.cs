using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Conversation search over the caller's chats (titles, channel usernames,
/// DM peer names). Local in-memory filter over the membership list — bounded
/// at 200 conversations, no extra index needed.
/// </summary>
public sealed record SearchMyChatsQuery(string Query, int Limit) : IRequest<Result<IReadOnlyList<ChatDto>>>;

public sealed class SearchMyChatsValidator : AbstractValidator<SearchMyChatsQuery>
{
    public SearchMyChatsValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2).MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class SearchMyChatsHandler(
    IChatStore chats,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<SearchMyChatsQuery, Result<IReadOnlyList<ChatDto>>>
{
    public async Task<Result<IReadOnlyList<ChatDto>>> Handle(SearchMyChatsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<ChatDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var needle = request.Query.Trim().ToLowerInvariant();
        var mine = await chats.ListForUserAsync(currentUser.UserId, 200, ct).ConfigureAwait(false);

        // One user batch for matching (DM peers) and projection: the old code
        // fetched peers, then re-fetched the same ids with all member ids.
        var names = await ChatQueries.UserMapAsync(
            users, mine.SelectMany(c => c.Members.Select(m => m.UserId)), ct).ConfigureAwait(false);

        var matched = mine
            .Where(c => !c.IsDeleted && Matches(c, needle, names, currentUser.UserId))
            .Take(request.Limit)
            .ToList();

        return Result<IReadOnlyList<ChatDto>>.Ok(
            matched.Select(c => MessagingMapping.ToDto(c, names, currentUser.UserId)).ToList());
    }

    private static bool Matches(
        Chat chat,
        string needle,
        Dictionary<string, (string Username, string DisplayName)> names,
        string callerId)
    {
        if (chat.Title is not null && chat.Title.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (chat.Username is not null && chat.Username.Contains(needle, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (chat.Type == ChatType.Dm)
        {
            var peer = chat.Members.FirstOrDefault(m => m.UserId != callerId);
            if (peer is not null && names.TryGetValue(peer.UserId, out var name))
            {
                return name.Username.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                    name.DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }
}
