namespace MyApp.Messaging.IntegrationEvents;

public sealed record ProductCreatedIntegrationEvent(
    Guid ProductId,
    string Name,
    decimal Price) : IntegrationEvent;
