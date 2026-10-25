using Microsoft.EntityFrameworkCore;
using MyApp.Messaging.IntegrationEvents;
using MyApp.ShippingApi.Persistence;
using Shared.Application.Persistence;

namespace MyApp.ShippingApi.Consumers.CreateShipmentOnOrderPlaced;

public sealed class OrderPlacedIntegrationEventHandler
{
    public async Task Handle(
        OrderPlacedIntegrationEvent message,
        ShippingDbContext db,
        CancellationToken ct)
    {
        var exists = await db.Shipments.AnyAsync(s => s.OrderId == message.OrderId, ct);
        if (exists)
            return;

        db.Shipments.Add(new Shipment
        {
            OrderId = message.OrderId,
            CustomerId = message.CustomerId,
            Status = ShipmentStatus.Pending,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
        }
    }
}
