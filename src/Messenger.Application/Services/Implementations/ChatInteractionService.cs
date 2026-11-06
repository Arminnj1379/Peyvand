using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Features.Chats;
using Messenger.Application.Messaging;
using Messenger.Application.Services.Interfaces;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Services.Implementations;

/// <summary>Member-level conversation interaction. Logic moved verbatim from the former MediatR handlers.</summary>
public sealed class ChatInteractionService(
    IChatStore chats,
    IMessageStore messages,
    IUserStore users,
    IClock clock) : IChatInteractionService
{
    public async Task<Result<ChatDto>> SetMuteAsync(
        SetMuteCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SetMuteValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

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

        var updated = member.Value!.SetMemberMute(caller.UserId, command.Muted, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> SetArchiveAsync(
        SetArchiveCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new SetArchiveValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

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

        var updated = member.Value!.SetMemberArchived(caller.UserId, command.Archived, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> MarkUnreadAsync(
        MarkUnreadCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new MarkUnreadValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

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

        var updated = member.Value!.MarkUnread(caller.UserId, command.Seq, clock.UtcNow);
        if (updated.IsFailure)
        {
            return Result<ChatDto>.Fail(updated.Error);
        }

        await chats.UpdateAsync(member.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> PinMessageAsync(
        PinMessageCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new PinMessageValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        // Pinning is a moderation action: members can pin in DMs, admins in groups/channels.
        var authorized = chat?.Type == ChatType.Dm
            ? ChatAccess.RequireMember(chat, caller.UserId)
            : ChatAccess.RequireAdmin(chat, caller.UserId);
        if (authorized.IsFailure)
        {
            return Result<ChatDto>.Fail(authorized.Error);
        }

        var message = await messages.GetByIdAsync(command.MessageId, ct).ConfigureAwait(false);
        if (message is null || message.ChatId != command.ChatId || message.IsDeleted)
        {
            return Result<ChatDto>.Fail(MessageErrors.NotFound);
        }

        var pinned = authorized.Value!.Pin(message.Id, clock.UtcNow);
        if (pinned.IsFailure)
        {
            return Result<ChatDto>.Fail(pinned.Error);
        }

        await chats.UpdateAsync(authorized.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, authorized.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> UnpinMessageAsync(
        UnpinMessageCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new UnpinMessageValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

        if (caller.UserId is null)
        {
            return Result<ChatDto>.Fail(ChatAccess.Unauthenticated());
        }

        var chat = await chats.GetByIdAsync(command.ChatId, ct).ConfigureAwait(false);
        var authorized = chat?.Type == ChatType.Dm
            ? ChatAccess.RequireMember(chat, caller.UserId)
            : ChatAccess.RequireAdmin(chat, caller.UserId);
        if (authorized.IsFailure)
        {
            return Result<ChatDto>.Fail(authorized.Error);
        }

        var unpinned = authorized.Value!.Unpin(command.MessageId, clock.UtcNow);
        if (unpinned.IsFailure)
        {
            return Result<ChatDto>.Fail(unpinned.Error);
        }

        await chats.UpdateAsync(authorized.Value, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, authorized.Value, caller.UserId, ct).ConfigureAwait(false));
    }

    public async Task<Result<ChatDto>> MarkReadAsync(
        MarkReadCommand command, CallerContext caller, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(caller);
        await new MarkReadValidator().ValidateAndThrowAsync(command, ct).ConfigureAwait(false);

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

        var now = clock.UtcNow;
        // Atomic compare-and-set in the store (safe under concurrent readers);
        // the in-memory copy feeds the response projection.
        await chats.MarkReadAsync(chat!.Id, caller.UserId, command.Seq, now, ct).ConfigureAwait(false);
        member.Value!.MarkRead(caller.UserId, command.Seq, now);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, member.Value, caller.UserId, ct).ConfigureAwait(false));
    }
}
