using FluentValidation;
using Messenger.Domain.Chats;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Deletes a group/channel (soft-delete: the chat doc stays, history is
/// preserved, lists and member routes read it as not-found). Owner-only;
/// DMs have fixed membership and cannot be deleted this way.
/// </summary>
public sealed record DeleteGroupCommand(string ChatId);

public sealed class DeleteGroupValidator : AbstractValidator<DeleteGroupCommand>
{
    public DeleteGroupValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}
