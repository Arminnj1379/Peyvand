namespace Messenger.Application.Abstractions;

/// <summary>
/// Raced-insert signal for the unique username/phone claims, mapped by
/// handlers to 409 Taken errors. Infrastructure-owned throw, Application-owned
/// type (dependency rule; mirrors <see cref="DuplicateChatException"/>).
/// Moved here from Infrastructure so the API middleware can catch it without
/// reaching past the port boundary.
/// </summary>
public sealed class DuplicateUserException(Domain.Common.Error error) : Exception(error.Message)
{
    public Domain.Common.Error Error { get; } = error;
}
