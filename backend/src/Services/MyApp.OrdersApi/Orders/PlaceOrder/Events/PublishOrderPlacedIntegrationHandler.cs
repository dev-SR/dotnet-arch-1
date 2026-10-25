using MyApp.Messaging.IntegrationEvents;
using Wolverine.EntityFrameworkCore;

namespace MyApp.OrdersApi.Orders.PlaceOrder.Events;

/// <summary>
/// Domain→integration bridge for <see cref="OrderPlacedDomainEvent"/>: publishes the public
/// <see cref="OrderPlacedIntegrationEvent"/> on the transactional outbox.
/// </summary>
public sealed class PublishOrderPlacedIntegrationHandler(IDbContextOutbox outbox)
    : INotificationHandler<OrderPlacedDomainEvent>
{
    public async ValueTask Handle(
        OrderPlacedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        await outbox.PublishAsync(new OrderPlacedIntegrationEvent(
            notification.OrderId,
            notification.CustomerId,
            notification.Total,
            notification.Lines.Select(l => new OrderLineDto(
                l.ProductId, l.ProductName, l.Quantity, l.UnitPrice)).ToList()));
    }
}
