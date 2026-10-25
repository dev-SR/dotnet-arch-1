using MyApp.Messaging.IntegrationEvents;
using Wolverine.EntityFrameworkCore;

namespace MyApp.Modules.Catalog.Products.CreateProduct.Events;

/// <summary>
/// Domain→integration bridge for <see cref="ProductCreatedDomainEvent"/>: publishes the public
/// <see cref="ProductCreatedIntegrationEvent"/> on the transactional outbox.
/// </summary>
public sealed class PublishProductCreatedIntegrationHandler(IDbContextOutbox outbox)
    : INotificationHandler<ProductCreatedDomainEvent>
{
    public async ValueTask Handle(
        ProductCreatedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        await outbox.PublishAsync(
            new ProductCreatedIntegrationEvent(notification.ProductId, notification.Name, notification.Price));
    }
}
