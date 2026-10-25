namespace MyApp.Messaging.IntegrationEvents;

public sealed record OrderLineDto(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);

public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Total,
    IReadOnlyList<OrderLineDto> Lines) : IntegrationEvent;
