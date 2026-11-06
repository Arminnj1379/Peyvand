using FluentValidation;

namespace Messenger.Application.Features.Users;

/// <summary>
/// Directory search. Employees search everyone; customers search employees only.
/// </summary>
public sealed record SearchUsersQuery(string Query, int Limit);

public sealed class SearchUsersValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2).MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}
