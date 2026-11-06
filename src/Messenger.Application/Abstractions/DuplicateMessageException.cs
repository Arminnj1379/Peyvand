namespace Messenger.Application.Abstractions;

/// <summary>
/// Raised by <see cref="IMessageStore.AddAsync"/> when the sender reuses a
/// <c>clientMessageId</c> concurrently. Handlers map it to the stored winner.
/// Infrastructure-owned throw, Application-owned type (dependency rule).
/// </summary>
public sealed class DuplicateMessageException : Exception
{
    public DuplicateMessageException()
        : base("Duplicate client message id.")
    {
    }
}
