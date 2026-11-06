using Messenger.Domain.Common;

namespace Messenger.Domain.Users;

/// <summary>Raised when a new account is created (welcome flows, audit).</summary>
public sealed record UserRegistered(string UserId, string Username, UserType UserType, DateTime At)
    : IDomainEvent
{
    public DateTime OccurredAt => At;
}
