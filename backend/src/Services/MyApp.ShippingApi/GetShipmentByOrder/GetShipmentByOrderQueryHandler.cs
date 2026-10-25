using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.ShippingApi.Persistence;
using Shared.Common.Errors;

namespace MyApp.ShippingApi.GetShipmentByOrder;

internal sealed class GetShipmentByOrderQueryHandler(ShippingDbContext db)
    : IRequestHandler<GetShipmentByOrderQuery, ErrorOr<ShipmentDto>>
{
    public async ValueTask<ErrorOr<ShipmentDto>> Handle(
        GetShipmentByOrderQuery query,
        CancellationToken cancellationToken)
    {
        var shipment = await db.Shipments.AsNoTracking()
            .SingleOrDefaultAsync(s => s.OrderId == query.OrderId, cancellationToken);

        if (shipment is null)
            return Error.NotFound(
                "SHIPMENT.NOT_FOUND",
                $"No shipment for order '{query.OrderId}'.");

        if (shipment.CustomerId != query.RequesterId)
            return Error.Forbidden(
                "SHIPMENT.FORBIDDEN",
                "You are not allowed to view this shipment.");

        return new ShipmentDto(
            shipment.Id,
            shipment.OrderId,
            shipment.CustomerId,
            shipment.Status.ToString(),
            shipment.CreatedAt);
    }
}
