using Microsoft.EntityFrameworkCore;
using MyApp.Messaging.IntegrationEvents;
using MyApp.OrdersApi.Catalog;
using MyApp.OrdersApi.Persistence;
using Shared.Application.Persistence;

namespace MyApp.OrdersApi.Consumers.ProjectProductCatalog;

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
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
        }
    }
}
