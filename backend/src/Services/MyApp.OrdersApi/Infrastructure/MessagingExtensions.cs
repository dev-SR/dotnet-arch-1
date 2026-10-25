using MyApp.Messaging;
using MyApp.Messaging.IntegrationEvents;
using MyApp.OrdersApi.Consumers.ProjectProductCatalog;
using MyApp.OrdersApi.Persistence;
using Wolverine.Persistence.Durability;

namespace MyApp.OrdersApi.Infrastructure;

public static class MessagingExtensions
{
    public static IHostApplicationBuilder UseOrdersApiMessaging(
        this IHostApplicationBuilder builder)
    {
        builder.UseTransactionalMessaging(msg =>
        {
            msg.UseRabbitMq(TransactionalMessagingOptions.RequireRabbitMq(builder.Configuration));
            msg.AddStore<OrdersDbContext>(
                TransactionalMessagingOptions.RequireConnectionString(builder.Configuration, "Orders"),
                MessageStoreRole.Main);
            msg.ListenTo(MessagingQueues.OrdersProducts, MessagingQueues.ProductEventsExchange);
            msg.PublishToExchange<OrderPlacedIntegrationEvent>(MessagingQueues.OrderEventsExchange);
            msg.IncludeHandler<ProductCreatedIntegrationEventHandler>();
            msg.IncludeHandler<ProductUpdatedIntegrationEventHandler>();
        });
        return builder;
    }
}
