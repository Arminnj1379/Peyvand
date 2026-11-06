using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Deletes a group/channel (soft-delete: the chat doc stays, history is
/// preserved, lists and member routes read it as not-found). Owner-only;
/// DMs have fixed membership and cannot be deleted this way.
/// </summary>
public sealed record DeleteGroupCommand(string ChatId) : IRequest<Result<ChatDto>>;

public sealed class DeleteGroupValidator : AbstractValidator<DeleteGroupCommand>
{
    public DeleteGroupValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class DeleteGroupHandler(
    IChatStore chats,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<DeleteGroupCommand, Result<ChatDto>>
{
    public async Task<Result<ChatDto>> Handle(DeleteGroupCommand request, CancellationToken ct)
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

        if (chat!.Type == ChatType.Dm)
        {
            return Result<ChatDto>.Fail(ChatErrors.DmMembershipFixed);
        }

        if (!string.Equals(chat.OwnerId, currentUser.UserId, StringComparison.Ordinal))
        {
            return Result<ChatDto>.Fail(ChatErrors.OnlyOwner);
        }

        chat.SoftDelete(clock.UtcNow);
        await chats.UpdateAsync(chat, ct).ConfigureAwait(false);
        return Result<ChatDto>.Ok(
            await ChatQueries.EnrichAsync(chats, users, chat, currentUser.UserId, ct).ConfigureAwait(false));
    }
}
