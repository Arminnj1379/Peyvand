using FluentValidation;

namespace Messenger.Application.Features.Chats;

/// <summary>
/// Conversation search over the caller's chats (titles, channel usernames,
/// DM peer names). Local in-memory filter over the membership list — bounded
/// at 200 conversations, no extra index needed.
/// </summary>
public sealed record SearchMyChatsQuery(string Query, int Limit);

public sealed class SearchMyChatsValidator : AbstractValidator<SearchMyChatsQuery>
{
    public SearchMyChatsValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2).MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}
