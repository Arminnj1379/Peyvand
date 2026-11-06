using FluentValidation;
using Messenger.Application.Abstractions;
using Messenger.Application.Features.Files;
using Messenger.Application.Messaging;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

/// <summary>Shared message enrichment (sender names + reply previews).</summary>
internal static class MessageQueries
{
    public static async Task<MessageDto> EnrichAsync(
        IMessageStore messages,
        IFileStore files,
        IUserStore users,
        Message message,
        CancellationToken ct,
        string? callerId = null)
    {
        ArgumentNullException.ThrowIfNull(message);
        string? username = null;
        string? name = null;
        if (message.SenderId is not null)
        {
            var sender = await users.GetByIdAsync(message.SenderId, ct).ConfigureAwait(false);
            username = sender?.Username;
            name = sender?.DisplayName;
        }

        ReplyPreviewDto? reply = null;
        if (message.Kind == MessageKind.Reply && message.ReplyToMessageId is not null)
        {
            var target = await messages.GetByIdAsync(message.ReplyToMessageId, ct).ConfigureAwait(false);
            if (target is not null && target.ChatId == message.ChatId)
            {
                string? targetName = null;
                if (target.SenderId is not null)
                {
                    var targetSender = await users.GetByIdAsync(target.SenderId, ct).ConfigureAwait(false);
                    targetName = targetSender?.DisplayName;
                }

                reply = new ReplyPreviewDto(
                    target.Id, targetName,
                    target.IsDeleted ? "Message deleted" : Message.PreviewOf(target.Body),
                    target.IsDeleted);
            }
        }

        return MessagingMapping.ToDto(message, username, name, reply, callerId,
            await AttachmentForAsync(files, message, ct).ConfigureAwait(false));
    }

    public static async Task<IReadOnlyList<MessageDto>> EnrichManyAsync(
        IMessageStore messages,
        IFileStore files,
        IUserStore users,
        IReadOnlyList<Message> items,
        CancellationToken ct,
        string? callerId = null)
    {
        // Resolve senders in one round-trip (was: sequential GetById per sender).
        var senderIds = items
            .Where(m => m.SenderId is not null)
            .Select(m => m.SenderId!)
            .Distinct()
            .ToList();
        var names = new Dictionary<string, (string Username, string DisplayName)>();
        foreach (var user in await users.ListByIdsAsync(senderIds, ct).ConfigureAwait(false))
        {
            names[user.Id] = (user.Username, user.DisplayName);
        }

        foreach (var id in senderIds)
        {
            names.TryAdd(id, ("unknown", "Unknown"));
        }

        var replyIds = items
            .Where(m => m.Kind == MessageKind.Reply && m.ReplyToMessageId is not null)
            .Select(m => m.ReplyToMessageId!)
            .Distinct()
            .ToList();
        // Reply targets in one round-trip (was: sequential GetById per reply).
        var replyTargets = new Dictionary<string, Message>();
        if (replyIds.Count > 0)
        {
            // Replies live in the same chats as the page; scope per target chat
            // below (ChatId match) so cross-chat id reuse can never leak.
            var targets = await messages.ListByIdsAsync(replyIds, ct).ConfigureAwait(false);
            var unseenSenderIds = new List<string>();
            foreach (var target in targets)
            {
                replyTargets[target.Id] = target;
                if (target.SenderId is not null && !names.ContainsKey(target.SenderId))
                {
                    unseenSenderIds.Add(target.SenderId);
                }
            }

            if (unseenSenderIds.Count > 0)
            {
                foreach (var user in await users.ListByIdsAsync(unseenSenderIds, ct).ConfigureAwait(false))
                {
                    names[user.Id] = (user.Username, user.DisplayName);
                }

                foreach (var id in unseenSenderIds.Distinct())
                {
                    names.TryAdd(id, ("unknown", "Unknown"));
                }
            }
        }

        // Resolve attachments in one pass (files referenced by this page).
        var attachments = new Dictionary<string, AttachmentDto>();
        var fileIds = items
            .Where(m => m.FileId is not null)
            .Select(m => m.FileId!)
            .Distinct()
            .ToList();
        if (fileIds.Count > 0)
        {
            var found = await files.ListByIdsAsync(fileIds, ct).ConfigureAwait(false);
            foreach (var f in found)
            {
                attachments[f.Id] = Features.Files.FileAccess.ToAttachmentDto(f);
            }
        }

        return items.Select(m =>
        {
            names.TryGetValue(m.SenderId ?? string.Empty, out var sender);
            ReplyPreviewDto? reply = null;
            if (m.Kind == MessageKind.Reply && m.ReplyToMessageId is not null &&
                replyTargets.TryGetValue(m.ReplyToMessageId, out var target) &&
                target.ChatId == m.ChatId)
            {
                names.TryGetValue(target.SenderId ?? string.Empty, out var targetSender);
                reply = new ReplyPreviewDto(
                    target.Id,
                    targetSender.DisplayName,
                    target.IsDeleted ? "Message deleted" : Message.PreviewOf(target.Body),
                    target.IsDeleted);
            }

            attachments.TryGetValue(m.FileId ?? string.Empty, out var attachment);
            return MessagingMapping.ToDto(m, sender.Username, sender.DisplayName, reply, callerId, attachment);
        }).ToList();
    }

    private static async Task<AttachmentDto?> AttachmentForAsync(
        IFileStore files, Message message, CancellationToken ct)
    {
        if (message.FileId is null)
        {
            return null;
        }

        var file = await files.GetByIdAsync(message.FileId, ct).ConfigureAwait(false);
        return file is null ? null : Features.Files.FileAccess.ToAttachmentDto(file);
    }
}

public sealed record GetHistoryQuery(string ChatId, long? BeforeSeq, int Limit);

public sealed record SyncMessagesQuery(string ChatId, long AfterSeq, int Limit);

public sealed record ListPinnedQuery(string ChatId);

public sealed class GetHistoryValidator : AbstractValidator<GetHistoryQuery>
{
    public GetHistoryValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.BeforeSeq).GreaterThanOrEqualTo(0).When(x => x.BeforeSeq.HasValue);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}

public sealed class SyncMessagesValidator : AbstractValidator<SyncMessagesQuery>
{
    public SyncMessagesValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.AfterSeq).GreaterThanOrEqualTo(0);
        RuleFor(x => x.Limit).InclusiveBetween(1, 200);
    }
}

public sealed class ListPinnedValidator : AbstractValidator<ListPinnedQuery>
{
    public ListPinnedValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}
