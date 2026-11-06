namespace Messenger.Domain.Common;

/// <summary>
/// Base class for future aggregates (User, Chat, Message, ...).
/// Identity is assigned by <c>IIdGenerator</c> (ObjectId in Infrastructure),
/// never inside Domain, so this layer keeps zero infrastructure dependencies.
/// </summary>
public abstract class Entity
{
    protected Entity(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        Id = id;
    }

    public string Id { get; }

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);

    public void ClearDomainEvents() => _domainEvents.Clear();
}
