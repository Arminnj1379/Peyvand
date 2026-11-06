using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Features.Chats;
using Messenger.Application.Messaging;
using Messenger.Application.Services.Interfaces;
using Messenger.Application.Users;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Services.Implementations;

/// <summary>Conversation lifecycle and queries. Logic moved verbatim from the former MediatR handlers.</summary>
public sealed class ConversationService(
    IChatStore chats,
    IUserStore users,
    IIdGenerator ids,
    IClock clock) : IConversationService
{
    public async Task<Result<ChatDto>> CreateDmAsync(
        CreateDmCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new CreateDmValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = caller.UserId;
        if (command.OtherUserId == callerId)
        {
            return Result<ChatDto>.Fail(ChatErrors.CannotDmSelf);
        }

        var other = await users.GetByIdAsync(command.OtherUserId, ct).ConfigureAwait(false);
        if (other is null || !other.IsActive)
        {
            return Result<ChatDto>.Fail(UserErrors.NotFound);
        }

        // Blocks are enforced in both directions without revealing who blocked:
        // no new DM can be started across a block.
        var self = await users.GetByIdAsync(callerId, ct).ConfigureAwait(false);
        if (self is not null &&
            (self.BlockedUserIds.Contains(other.Id) || other.BlockedUserIds.Contains(callerId)))
        {
            return Result<ChatDto>.Fail(ChatErrors.Blocked);
        }

        var existing = await chats.FindDmAsync(callerId, other.Id, ct).ConfigureAwait(false);
        if (existing is not null && !existing.IsDeleted)
        {
            return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, existing, callerId, ct).ConfigureAwait(false));
        }

        var now = clock.UtcNow;
        var created = Chat.CreateDm(ids.NewId(), callerId, other.Id, now);
        if (created.IsFailure)
        {
            return Result<ChatDto>.Fail(created.Error);
        }

        try
        {
            await chats.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateChatException)
        {
            // Lost the insert race on the deterministic pair key: the winner
            // holds the conversation — return it (idempotent create).
            var winner = await chats.FindDmAsync(callerId, other.Id, ct).ConfigureAwait(false);
            if (winner is null || winner.IsDeleted)
            {
                return Result<ChatDto>.Fail(ChatErrors.DmExists);
            }

            return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, winner, callerId, ct).ConfigureAwait(false));
        }

        return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, created.Value!, callerId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> CreateGroupAsync(
        CreateGroupCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new CreateGroupValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = caller.UserId;
        var distinct = command.MemberIds
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToList();
        if (!distinct.Contains(callerId))
        {
            distinct.Insert(0, callerId);
        }

        // Every member must be a real, active user (ids come from the picker, never trusted blindly).
        var members = await users.ListByIdsAsync(distinct, ct).ConfigureAwait(false);
        var byId = members.ToDictionary(u => u.Id);
        foreach (var memberId in distinct)
        {
            if (!byId.TryGetValue(memberId, out var user) || !user.IsActive)
            {
                return Result<ChatDto>.Fail(UserErrors.NotFound);
            }
        }

        var now = clock.UtcNow;
        var created = Chat.CreateGroup(ids.NewId(), callerId, command.Title, distinct, command.Description, now);
        if (created.IsFailure)
        {
            return Result<ChatDto>.Fail(created.Error);
        }

        await chats.AddAsync(created.Value!, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, created.Value!, callerId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> CreateChannelAsync(
        CreateChannelCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new CreateChannelValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = caller.UserId;
        var subs = (command.SubscriberIds ?? []).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList();
        // One round-trip for the whole subscriber list (was: GetById per id).
        var found = await users.ListByIdsAsync(subs, ct).ConfigureAwait(false);
        var byId = found.ToDictionary(u => u.Id);
        foreach (var sub in subs)
        {
            if (!byId.TryGetValue(sub, out var user) || !user.IsActive)
            {
                return Result<ChatDto>.Fail(UserErrors.NotFound);
            }
        }

        if (command.IsPublic)
        {
            if (string.IsNullOrWhiteSpace(command.Username) || !Chat.IsValidUsername(command.Username))
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameInvalid);
            }

            var taken = await chats.GetByUsernameAsync(command.Username, ct).ConfigureAwait(false);
            if (taken is not null)
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
            }
        }

        var now = clock.UtcNow;
        var created = Chat.CreateChannel(
            ids.NewId(), callerId, command.Title, command.Description, subs, now,
            command.IsPublic, command.Username);
        if (created.IsFailure)
        {
            return Result<ChatDto>.Fail(created.Error);
        }

        try
        {
            await chats.AddAsync(created.Value!, ct).ConfigureAwait(false);
        }
        catch (DuplicateChatException)
        {
            // Lost a concurrent claim on the public username.
            return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
        }

        return Result<ChatDto>.Ok(await ChatQueries.EnrichAsync(chats, users, created.Value!, callerId, ct).ConfigureAwait(false));
    }

    public async Task<Result<IReadOnlyList<ChatDto>>> ListMyChatsAsync(
        ListMyChatsQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new ListMyChatsValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<ChatDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var found = await chats.ListForUserAsync(caller.UserId, query.Limit, ct).ConfigureAwait(false);
        // One user batch for all chats (was: per-chat member loop → O(chats ×
        // members) sequential lookups with cross-chat duplicates re-fetched).
        var map = await ChatQueries.UserMapAsync(
            users, found.SelectMany(c => c.Members.Select(m => m.UserId)), ct).ConfigureAwait(false);
        var dtos = found.Select(c => MessagingMapping.ToDto(c, map, caller.UserId)).ToList();
        return Result<IReadOnlyList<ChatDto>>.Ok(dtos);
    }

    public async Task<Result<IReadOnlyList<ChatDto>>> SearchMyChatsAsync(
        SearchMyChatsQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new SearchMyChatsValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<IReadOnlyList<ChatDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var needle = query.Query.Trim().ToLowerInvariant();
        var mine = await chats.ListForUserAsync(caller.UserId, 200, ct).ConfigureAwait(false);

        // One user batch for matching (DM peers) and projection: the old code
        // fetched peers, then re-fetched the same ids with all member ids.
        var names = await ChatQueries.UserMapAsync(
            users, mine.SelectMany(c => c.Members.Select(m => m.UserId)), ct).ConfigureAwait(false);

        var matched = mine
            .Where(c => !c.IsDeleted && Matches(c, needle, names, caller.UserId))
            .Take(query.Limit)
            .ToList();

        return Result<IReadOnlyList<ChatDto>>.Ok(
            matched.Select(c => MessagingMapping.ToDto(c, names, caller.UserId)).ToList());
    }

    public async Task<Result<ChatDto>> GetChatAsync(
        GetChatQuery query, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(caller);
        await new GetChatValidator().ValidateAndThrowAsync(query, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(query.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<ChatDto>.Fail(member.Error);
        }

        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value!, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> UpdateGroupAsync(
        UpdateGroupCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UpdateGroupValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        var updated = admin.Value!.UpdateGroupProfile(
            command.Title, command.Description, command.MembersCanInvite, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> UpdateChannelAsync(
        UpdateChannelCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UpdateChannelValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        var updated = admin.Value!.UpdateChannelProfile(command.Title, command.Description, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> SetChannelVisibilityAsync(
        SetChannelVisibilityCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SetChannelVisibilityValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var admin = ChatAccess.RequireAdmin(chat, caller.UserId);
        if (admin.IsFailure)
        {
            return Result<ChatDto>.Fail(admin.Error);
        }

        if (command.IsPublic)
        {
            if (string.IsNullOrWhiteSpace(command.Username) || !Chat.IsValidUsername(command.Username))
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameInvalid);
            }

            // Self-claim is fine; anyone else holding it is a conflict.
            var holder = await chats.GetByUsernameAsync(command.Username, ct).ConfigureAwait(false);
            if (holder is not null && holder.Id != admin.Value!.Id)
            {
                return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
            }
        }

        var updated = admin.Value!.SetVisibility(command.IsPublic, command.Username, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        try
        {
            await chats.UpdateAsync(admin.Value, ct).ConfigureAwait(false);
        }
        catch (DuplicateChatException)
        {
            return Result<ChatDto>.Fail(ChatErrors.UsernameTaken);
        }

        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, admin.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> DeleteGroupAsync(
        DeleteGroupCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new DeleteGroupValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var member = ChatAccess.RequireMember(chat, caller.UserId);
        if (member.IsFailure)
        {
            return Result<ChatDto>.Fail(member.Error);
        }

        if (chat!.Type == ChatType.Dm)
        {
            return Result<ChatDto>.Fail(ChatErrors.DmMembershipFixed);
        }

        if (!string.Equals(chat.OwnerId, caller.UserId, StringComparison.Ordinal))
        {
            return Result<ChatDto>.Fail(ChatErrors.OnlyOwner);
        }

        chat.SoftDelete(clock.UtcNow);
        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, chat, caller.UserId, ct).ConfigureAwait(false));
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
