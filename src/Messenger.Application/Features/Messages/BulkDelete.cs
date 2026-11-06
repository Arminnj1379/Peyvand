using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Messaging;
using Messenger.Domain.Chats;
using Messenger.Domain.Common;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Bulk delete for message selection flows. Per-message authorization mirrors
/// single delete: own messages always, others' messages only for chat
/// owners/admins. Already-deleted and unknown ids are skipped (not errors) so
/// a stale selection still succeeds; an empty selection is rejected.
/// The endpoint fans out one <c>message:deleted</c> event per message so live
/// clients reconcile through the same path as single deletes.
/// </summary>
public sealed record BulkDeleteMessagesCommand(IReadOnlyList<string> MessageIds)
    : IRequest<Result<IReadOnlyList<MessageDto>>>;

public sealed class BulkDeleteMessagesValidator : AbstractValidator<BulkDeleteMessagesCommand>
{
    public BulkDeleteMessagesValidator()
    {
        RuleFor(x => x.MessageIds).NotNull().Must(ids => ids.Count is >= 1 and <= 50)
            .WithMessage("Select between 1 and 50 messages.");
    }
}

public sealed class BulkDeleteMessagesHandler(
    IChatStore chats,
    IMessageStore messages,
    IFileStore files,
    IUserStore users,
    IClock clock,
    ICurrentUser currentUser) : IRequestHandler<BulkDeleteMessagesCommand, Result<IReadOnlyList<MessageDto>>>
{
    public async Task<Result<IReadOnlyList<MessageDto>>> Handle(
        BulkDeleteMessagesCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<MessageDto>>.Fail(ChatAccess.Unauthenticated());
        }

        var callerId = currentUser.UserId;
        var now = clock.UtcNow;
        var deleted = new List<MessageDto>();
        var deletedMessages = new List<Message>();

        // Group by chat so each conversation authorizes once. Targets arrive in
        // one round-trip (was: sequential GetById per selected id).
        var targets = (await messages.ListByIdsAsync(
                request.MessageIds.Distinct().Take(50).ToList(), ct).ConfigureAwait(false))
            .Where(m => !m.IsDeleted)
            .ToList();

        foreach (var group in targets.GroupBy(m => m.ChatId))
        {
            var chat = await chats.GetByIdAsync(group.Key, ct).ConfigureAwait(false);
            var member = ChatAccess.RequireMember(chat, callerId);
            if (member.IsFailure)
            {
                return Result<IReadOnlyList<MessageDto>>.Fail(member.Error);
            }

            // Authorize everything before mutating anything: a stale selection
            // must never leave a partial delete behind.
            var isAdmin = member.Value!.IsAdmin(callerId);
            foreach (var message in group)
            {
                if (message.SenderId != callerId && !isAdmin)
                {
                    return Result<IReadOnlyList<MessageDto>>.Fail(MessageErrors.CannotDelete);
                }
            }

            foreach (var message in group)
            {
                var removed = message.Delete(callerId, now);
                if (removed.IsFailure)
                {
                    continue;
                }

                await messages.UpdateAsync(message, ct).ConfigureAwait(false);
                deletedMessages.Add(message);
            }
        }

        // Enrich the deleted set in one batched pass (was: per-message enrich
        // with repeated sender lookups).
        var enriched = await MessageQueries.EnrichManyAsync(messages, files, users, deletedMessages, ct, callerId)
            .ConfigureAwait(false);
        deleted.AddRange(enriched);

        return Result<IReadOnlyList<MessageDto>>.Ok(deleted);
    }
}
