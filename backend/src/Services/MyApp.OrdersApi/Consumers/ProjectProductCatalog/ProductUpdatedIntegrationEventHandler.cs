using Microsoft.EntityFrameworkCore;
using MyApp.Messaging.IntegrationEvents;
using MyApp.OrdersApi.Catalog;
using MyApp.OrdersApi.Persistence;

namespace MyApp.OrdersApi.Consumers.ProjectProductCatalog;

public sealed class ProductUpdatedIntegrationEventHandler
{
    public async Task Handle(
        ProductUpdatedIntegrationEvent message,
        OrdersDbContext db,
        CancellationToken ct)
    {
        var row = await db.ProductCatalog.SingleOrDefaultAsync(x => x.ProductId == message.ProductId, ct);
        if (row is null)
        {
            db.ProductCatalog.Add(new ProductCatalogItem
            {
                ProductId = message.ProductId,
                Name = message.Name,
                Price = message.Price,
                SyncedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.Name = message.Name;
            row.Price = message.Price;
            row.SyncedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }
}
