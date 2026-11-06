using FluentValidation;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

public sealed record EditMessageCommand(string MessageId, string Body);

public sealed record DeleteMessageCommand(string MessageId);

public sealed class EditMessageValidator : AbstractValidator<EditMessageCommand>
{
    public EditMessageValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
        // Empty captions are legal on attachment messages; the domain rejects
        // empty text-only messages (MessageErrors.InvalidBody).
        RuleFor(x => x.Body).NotNull().MaximumLength(Message.MaxBodyLength);
    }
}

public sealed class DeleteMessageValidator : AbstractValidator<DeleteMessageCommand>
{
    public DeleteMessageValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
    }
}
