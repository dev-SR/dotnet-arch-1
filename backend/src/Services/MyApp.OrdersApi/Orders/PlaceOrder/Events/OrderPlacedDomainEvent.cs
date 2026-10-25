using Shared.Domain;

namespace MyApp.OrdersApi.Orders.PlaceOrder.Events;

public sealed record OrderLineSnapshot(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);

public sealed record OrderPlacedDomainEvent(
    Guid OrderId,
    Guid CustomerId,
    decimal Total,
    IReadOnlyList<OrderLineSnapshot> Lines) : DomainEvent;
