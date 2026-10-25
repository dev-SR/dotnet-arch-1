using MyApp.Messaging;
using MyApp.ShippingApi.Consumers.CreateShipmentOnOrderPlaced;
using MyApp.ShippingApi.Persistence;
using Wolverine.Persistence.Durability;

namespace MyApp.ShippingApi.Infrastructure;

public static class MessagingExtensions
{
    public static IHostApplicationBuilder UseShippingApiMessaging(
        this IHostApplicationBuilder builder)
    {
        builder.UseTransactionalMessaging(msg =>
        {
            msg.UseRabbitMq(TransactionalMessagingOptions.RequireRabbitMq(builder.Configuration));
            msg.AddStore<ShippingDbContext>(
                TransactionalMessagingOptions.RequireConnectionString(builder.Configuration, "Shipping"),
                MessageStoreRole.Main);
            msg.ListenTo(MessagingQueues.ShippingOrders, MessagingQueues.OrderEventsExchange);
            msg.IncludeHandler<OrderPlacedIntegrationEventHandler>();
        });
        return builder;
    }
}
