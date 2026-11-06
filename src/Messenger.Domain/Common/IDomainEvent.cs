namespace Messenger.Domain.Common;

/// <summary>
/// Marker for in-process domain events. Dispatched after the unit of work
/// commits (e.g. MessageSent -> SignalR fan-out + push). No message bus in MVP.
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
