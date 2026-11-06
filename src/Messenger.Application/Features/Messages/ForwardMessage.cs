using FluentValidation;
using Messenger.Domain.Messages;

namespace Messenger.Application.Features.Messages;

/// <summary>Copies a message into another conversation with provenance attached.</summary>
public sealed record ForwardMessageCommand(
    string SourceChatId,
    string SourceMessageId,
    string TargetChatId,
    string ClientMessageId);

public sealed class ForwardMessageValidator : AbstractValidator<ForwardMessageCommand>
{
    public ForwardMessageValidator()
    {
        RuleFor(x => x.SourceChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.SourceMessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.TargetChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.ClientMessageId).NotEmpty().MaximumLength(64);
    }
}
