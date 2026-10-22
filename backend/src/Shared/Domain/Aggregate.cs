namespace Shared.Domain;

public abstract class Aggregate : Entity, IAggregate
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void AddDomainEvent(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    public IDomainEvent[] ClearDomainEvents()
    {
        var dequeued = _domainEvents.ToArray();
        _domainEvents.Clear();
        return dequeued;
    }
}
