using FluentValidation;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Per-member conversation preferences: mute, archive, and mark-unread.
/// All three are member-only and caller-relative — they never affect other
/// participants and need no admin rights.
/// </summary>
public sealed record SetMuteCommand(string ChatId, bool Muted);

public sealed record SetArchiveCommand(string ChatId, bool Archived);

public sealed record MarkUnreadCommand(string ChatId, long Seq);

public sealed class SetMuteValidator : AbstractValidator<SetMuteCommand>
{
    public SetMuteValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class SetArchiveValidator : AbstractValidator<SetArchiveCommand>
{
    public SetArchiveValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
    }
}

public sealed class MarkUnreadValidator : AbstractValidator<MarkUnreadCommand>
{
    public MarkUnreadValidator()
    {
        RuleFor(x => x.ChatId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Seq).GreaterThanOrEqualTo(1);
    }
}
