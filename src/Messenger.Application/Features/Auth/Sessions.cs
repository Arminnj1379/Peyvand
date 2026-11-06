using FluentValidation;
using MediatR;
using Messenger.Application.Abstractions;
using Messenger.Application.Auth;
using Messenger.Domain.Common;
using Messenger.Domain.Users;

namespace Messenger.Application.Features.Auth;

/// <summary>Session management for the current caller (from the JWT sid claim).</summary>
public sealed record LogoutCommand() : IRequest<Result>;

public sealed record LogoutAllCommand() : IRequest<Result>;

public sealed record ListSessionsQuery() : IRequest<Result<IReadOnlyList<SessionDto>>>;

public sealed record RevokeSessionCommand(string SessionId) : IRequest<Result>;

public sealed class RevokeSessionValidator : AbstractValidator<RevokeSessionCommand>
{
    public RevokeSessionValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty().MaximumLength(64);
    }
}

public sealed class LogoutHandler(IDeviceSessionStore sessions, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.SessionId is null || currentUser.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var session = await sessions.GetByIdAsync(currentUser.SessionId, ct).ConfigureAwait(false);
        if (session is not null && session.UserId == currentUser.UserId)
        {
            session.Revoke(clock.UtcNow);
            await sessions.UpdateAsync(session, ct).ConfigureAwait(false);
        }

        return Result.Ok();
    }
}

public sealed class LogoutAllHandler(IDeviceSessionStore sessions, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<LogoutAllCommand, Result>
{
    public async Task<Result> Handle(LogoutAllCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        await sessions.RevokeAllForUserAsync(currentUser.UserId, clock.UtcNow, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}

public sealed class ListSessionsHandler(IDeviceSessionStore sessions, ICurrentUser currentUser)
    : IRequestHandler<ListSessionsQuery, Result<IReadOnlyList<SessionDto>>>
{
    public async Task<Result<IReadOnlyList<SessionDto>>> Handle(ListSessionsQuery request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result<IReadOnlyList<SessionDto>>.Fail(AuthErrors.Unauthenticated);
        }

        var all = await sessions.ListByUserAsync(currentUser.UserId, 100, ct).ConfigureAwait(false);
        var dtos = all
            .OrderByDescending(s => s.LastUsedAt)
            .Select(s => new SessionDto(
                s.Id, s.DeviceLabel, s.IpAddress, s.CreatedAt, s.LastUsedAt,
                s.ExpiresAt, s.Id == currentUser.SessionId, s.IsRevoked))
            .ToList();
        return Result<IReadOnlyList<SessionDto>>.Ok(dtos);
    }
}

public sealed class RevokeSessionHandler(IDeviceSessionStore sessions, IClock clock, ICurrentUser currentUser)
    : IRequestHandler<RevokeSessionCommand, Result>
{
    public async Task<Result> Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (currentUser.UserId is null)
        {
            return Result.Fail(AuthErrors.Unauthenticated);
        }

        var session = await sessions.GetByIdAsync(request.SessionId, ct).ConfigureAwait(false);
        if (session is null || session.UserId != currentUser.UserId)
        {
            return Result.Fail(UserErrors.SessionNotFound);
        }

        session.Revoke(clock.UtcNow);
        await sessions.UpdateAsync(session, ct).ConfigureAwait(false);
        return Result.Ok();
    }
}
