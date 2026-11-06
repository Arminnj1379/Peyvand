namespace Messenger.Application.Abstractions;

/// <summary>
/// Raised by <see cref="ICallStore"/> implementations when a second live call
/// for the same chat collides concurrently. Handlers map it to the
/// domain-level conflict error. Infrastructure-owned throw, Application-owned
/// type (dependency rule; mirrors <see cref="DuplicateChatException"/>).
/// </summary>
public sealed class DuplicateCallException : Exception
{
    public DuplicateCallException()
        : base("Duplicate live call.")
    {
    }
}
