using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Application.Users;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Users;

/// <summary>
/// Profile reads. Employees see every active profile; customers see employees
/// only (customer-to-customer visibility arrives with the chats module).
/// </summary>
public sealed record GetMyProfileQuery() : IRequest<Result<UserDto>>;

public sealed record GetUserByIdQuery(string UserId) : IRequest<Result<UserDto>>;

public sealed class GetUserByIdValidator : AbstractValidator<GetUserByIdQuery>
{
    public GetUserByIdValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
    }
}

public sealed class GetMyProfileHandler(IUserStore users, ICurrentUser currentUser)
    : IRequestHandler<GetMyProfileQuery, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(GetMyProfileQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var user = await users.GetByIdAsync(currentUser.UserId, ct).ConfigureAwait(false);
        if (user is null || !user.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        return Result<UserDto>.Ok(UserMapping.ToDto(user, includePhone: true));
    }
}

public sealed class GetUserByIdHandler(
    IUserStore users,
    IChatStore chats,
    ICurrentUser currentUser)
    : IRequestHandler<GetUserByIdQuery, Result<UserDto>>
{
    public async Task<Result<UserDto>> Handle(GetUserByIdQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<UserDto>.Fail(AuthErrors.Unauthenticated);
        }

        var target = await users.GetByIdAsync(request.UserId, ct).ConfigureAwait(false);
        if (target is null || !target.IsActive)
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        if (!await ProfileVisibility.CanViewAsync(currentUser, target, chats, ct).ConfigureAwait(false))
        {
            return Result<UserDto>.Fail(UserErrors.NotFound);
        }

        var isSelf = target.Id == currentUser.UserId;
        return Result<UserDto>.Ok(UserMapping.ToDto(target, includePhone: isSelf));
    }
}
