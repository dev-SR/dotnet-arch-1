using MyApp.Messaging.IntegrationEvents;
using Wolverine.EntityFrameworkCore;

namespace MyApp.Modules.Catalog.Products.UpdateProduct.Events;

/// <summary>
/// Domain→integration bridge for <see cref="ProductUpdatedDomainEvent"/>: publishes the public
/// <see cref="ProductUpdatedIntegrationEvent"/> on the transactional outbox.
/// </summary>
public sealed class PublishProductUpdatedIntegrationHandler(IDbContextOutbox outbox)
    : INotificationHandler<ProductUpdatedDomainEvent>
{
    public async ValueTask Handle(
        ProductUpdatedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        await outbox.PublishAsync(
            new ProductUpdatedIntegrationEvent(notification.ProductId, notification.Name, notification.Price));
    }
}
