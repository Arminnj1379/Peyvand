using FluentValidation;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Emoji reactions (toggle) and reply threads (direct children, oldest-first).
/// Authorization: every member — including channel subscribers, who cannot post
/// — may react and read threads. Reactions on deleted messages are rejected.
/// </summary>
public sealed record ToggleReactionCommand(string MessageId, string Emoji);

public sealed record GetRepliesQuery(string MessageId, int Limit);

public sealed class ToggleReactionValidator : AbstractValidator<ToggleReactionCommand>
{
    public ToggleReactionValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Emoji).NotEmpty().MinimumLength(1).MaximumLength(16);
    }
}

public sealed class GetRepliesValidator : AbstractValidator<GetRepliesQuery>
{
    public GetRepliesValidator()
    {
        RuleFor(x => x.MessageId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 100);
    }
}
