using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

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

public sealed record ListMyChatsQuery(int Limit) : IRequest<Result<IReadOnlyList<ChatDto>>>;

public sealed class ListMyChatsValidator : AbstractValidator<ListMyChatsQuery>
{
    public ListMyChatsValidator()
    {
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class ListMyChatsHandler(
    IChatStore chats,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<ListMyChatsQuery, Result<IReadOnlyList<ChatDto>>>
{
    public async Task<Result<IReadOnlyList<ChatDto>>> Handle(ListMyChatsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<ChatDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var found = await chats.ListForUserAsync(currentUser.UserId, request.Limit, ct).ConfigureAwait(false);
        // One user batch for all chats (was: per-chat member loop → O(chats ×
        // members) sequential lookups with cross-chat duplicates re-fetched).
        var map = await ChatQueries.UserMapAsync(
            users, found.SelectMany(c => c.Members.Select(m => m.UserId)), ct).ConfigureAwait(false);
        var dtos = found.Select(c => MessagingMapping.ToDto(c, map, currentUser.UserId)).ToList();
        return Result<IReadOnlyList<ChatDto>>.Ok(dtos);
    }
}

public sealed record GetChatQuery(string ChatId) : IRequest<Result<ChatDto>>;

public sealed class GetChatValidator : AbstractValidator<GetChatQuery>
{
    public GetChatValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetChatHandler(
    IChatStore chats,
    IUserStore users,
    ICurrentUser currentUser) : IRequestHandler<GetChatQuery, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(GetChatQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(request.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, currentUser.UserId);
        if (member.IsFailure)
        {
            return Result<ChatDto>.Fail(member.Error);
        }

        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value!, currentUser.UserId, ct).ConfigureAwait(false));
    }
}
