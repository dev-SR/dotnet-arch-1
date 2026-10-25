namespace MyApp.Messaging.IntegrationEvents;

public sealed record ProductUpdatedIntegrationEvent(
    Guid ProductId,
    string Name,
    decimal Price) : IntegrationEvent;
