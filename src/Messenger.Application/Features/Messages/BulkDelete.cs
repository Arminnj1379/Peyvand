using FluentValidation;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Bulk delete for message selection flows. Per-message authorization mirrors
/// single delete: own messages always, others' messages only for chat
/// owners/admins. Already-deleted and unknown ids are skipped (not errors) so
/// a stale selection still succeeds; an empty selection is rejected.
/// The controller fans out one <c>message:deleted</c> event per message so live
/// clients reconcile through the same path as single deletes.
/// </summary>
public sealed record BulkDeleteMessagesCommand(IReadOnlyList<string> MessageIds);

public sealed class BulkDeleteMessagesValidator : AbstractValidator<BulkDeleteMessagesCommand>
{
    public BulkDeleteMessagesValidator()
    {
        RuleFor(x => x.MessageIds).NotNull().Must(ids => ids.Count is >= 1 and <= 50)
            .WithMessage("Select between 1 and 50 messages.");
    }
}
