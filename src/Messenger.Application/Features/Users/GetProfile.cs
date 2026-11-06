using FluentValidation;

namespace Messenger.Application.Features.Users;

/// <summary>
/// Profile reads. Employees see every active profile; customers see employees
/// only (customer-to-customer visibility arrives with the chats module).
/// </summary>
public sealed record GetMyProfileQuery();

public sealed record GetUserByIdQuery(string UserId);

public sealed class GetUserByIdValidator : AbstractValidator<GetUserByIdQuery>
{
    public GetUserByIdValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}
