using FluentValidation;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Sends a text or attachment message. Idempotent on <c>(sender, clientMessageId)</c>:
/// retries return the original message, never a duplicate.
/// Captions are optional when a file is attached; text messages still need a body.
/// Persist-then-broadcast: the controller fans out only after this succeeds.
/// </summary>
public sealed record SendMessageCommand(
    string ChatId,
    string Body,
    string ClientMessageId,
    string? ReplyToMessageId,
    string? FileId = null);

public sealed class SendMessageValidator : AbstractValidator<SendMessageCommand>
{
    public SendMessageValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        // Empty bodies are legal captions on attachment messages; the domain
        // rejects empty text-only messages (MessageErrors.InvalidBody).
        RuleFor(x => x.Body).NotNull().MaximumLength(Message.MaxBodyLength);
        RuleFor(x => x.ClientMessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ReplyToMessageId).MaximumLength(64);
        RuleFor(x => x.FileId).MaximumLength(64);
    }
}
