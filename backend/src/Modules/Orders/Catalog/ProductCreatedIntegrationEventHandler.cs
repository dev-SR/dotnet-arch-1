using Microsoft.EntityFrameworkCore;
using MyApp.Messaging.IntegrationEvents;
using MyApp.Modules.Orders.Persistence;
using Shared.Application.Persistence;

namespace MyApp.Modules.Orders.Catalog;

/// <summary>
/// Wolverine handler (not Mediator). Handler-level idempotency plus durable inbox.
/// </summary>
public sealed class ProductCreatedIntegrationEventHandler
{
    public async Task Handle(
        ProductCreatedIntegrationEvent message,
        OrdersDbContext db,
        CancellationToken ct)
    {
        var exists = await db.ProductCatalog.AnyAsync(x => x.ProductId == message.ProductId, ct);
        if (exists)
            return;

        db.ProductCatalog.Add(new ProductCatalogItem
        {
            ProductId = message.ProductId,
            Name = message.Name,
            Price = message.Price,
            SyncedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            // Ancillary-store DbContexts are not always auto-saved by Wolverine EF middleware.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            // Concurrent at-least-once delivery: another worker inserted first.
            db.ChangeTracker.Clear();
        }
    }
}
