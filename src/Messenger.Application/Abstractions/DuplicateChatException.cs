namespace Messenger.Application.Abstractions;

/// <summary>
/// Raised by <see cref="IChatStore"/> implementations when a unique chat claim
/// (channel username or invite code) collides concurrently. Handlers map it to
/// the domain-level conflict error. Infrastructure-owned throw, Application-owned
/// type (dependency rule; mirrors <see cref="DuplicateMessageException"/>).
/// </summary>
public sealed class DuplicateChatException : Exception
{
    public DuplicateChatException()
        : base("Duplicate chat claim.")
    {
    }
}
