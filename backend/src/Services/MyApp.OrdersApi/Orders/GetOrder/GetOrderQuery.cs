using Shared.Application.Abstractions.Queries;

namespace MyApp.OrdersApi.Orders.GetOrder;

public sealed record OrderLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPriceAtPurchase);

public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    IReadOnlyList<OrderLineDto> Items);

public sealed record GetOrderQuery(Guid OrderId, Guid RequesterId, bool IsAdmin) : IQuery<OrderDto>;
