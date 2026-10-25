using Shared.Application.Abstractions.Queries;

namespace MyApp.ShippingApi.GetShipmentByOrder;

public sealed record ShipmentDto(
    Guid Id,
    Guid OrderId,
    Guid CustomerId,
    string Status,
    DateTimeOffset CreatedAt);

public sealed record GetShipmentByOrderQuery(Guid OrderId, Guid RequesterId) : IQuery<ShipmentDto>;
