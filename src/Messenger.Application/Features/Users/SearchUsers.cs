using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Users;

/// <summary>
/// Directory search. Employees search everyone; customers search employees only.
/// </summary>
public sealed record SearchUsersQuery(string Query, int Limit) : IRequest<Result<IReadOnlyList<UserDto>>>;

public sealed class SearchUsersValidator : AbstractValidator<SearchUsersQuery>
{
    public SearchUsersValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MinimumLength(2).MaximumLength(64);
        RuleFor(x => x.Limit).InclusiveBetween(1, 50);
    }
}

public sealed class SearchUsersHandler(IUserStore users, ICurrentUser currentUser)
    : IRequestHandler<SearchUsersQuery, Result<IReadOnlyList<UserDto>>>
{
    public async Task<Result<IReadOnlyList<UserDto>>> Handle(SearchUsersQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<UserDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var callerIsEmployee = ProfileVisibility.IsEmployee(currentUser);

        var found = await users.SearchAsync(request.Query.Trim(), callerIsEmployee, request.Limit, ct)
            .ConfigureAwait(false);
        var dtos = found
            .Where(u => u.IsActive)
            .Select(u => UserMapping.ToDto(u, includePhone: u.Id == currentUser.UserId))
            .ToList();
        return Result<IReadOnlyList<UserDto>>.Ok(dtos);
    }
}
