namespace MyApp.Messaging;

/// <summary>
/// Cross-module / cross-service message contract. Distinct from in-process
/// <c>IDomainEvent</c> (Mediator notifications).
/// </summary>
public interface IIntegrationEvent
{
    Guid Id { get; }
    DateTimeOffset OccurredOn { get; }
}
