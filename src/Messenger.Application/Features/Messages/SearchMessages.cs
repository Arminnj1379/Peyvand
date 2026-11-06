using FluentValidation;

namespace Messenger.Application.Features.Messages;

/// <summary>
/// Global message search across the caller's conversations (MongoDB text
/// index on bodies). Results are newest-first; deleted messages are excluded
/// (their bodies are wiped). Membership is enforced by scoping to chat ids.
/// </summary>
public sealed record SearchMessagesQuery(string Query, int Limit);

public sealed class SearchMessagesValidator : AbstractValidator<SearchMessagesQuery>
{
    public SearchMessagesValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}
