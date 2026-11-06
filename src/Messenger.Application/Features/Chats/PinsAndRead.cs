using FluentValidation;

namespace Messenger.Application.Features.Chats;

public sealed record PinMessageCommand(string ChatId, string MessageId);

public sealed record UnpinMessageCommand(string ChatId, string MessageId);

public sealed record MarkReadCommand(string ChatId, long Seq);

public sealed class PinMessageValidator : AbstractValidator<PinMessageCommand>
{
    public PinMessageValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
    }
}

public sealed class UnpinMessageValidator : AbstractValidator<UnpinMessageCommand>
{
    public UnpinMessageValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
    }
}

public sealed class MarkReadValidator : AbstractValidator<MarkReadCommand>
{
    public MarkReadValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Seq).GreaterThanOrEqualTo(0);
    }
}
