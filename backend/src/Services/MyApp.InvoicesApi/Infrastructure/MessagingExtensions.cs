using MyApp.InvoicesApi.Consumers.CreateInvoiceOnOrderPlaced;
using MyApp.Messaging;
using Wolverine.Persistence.Durability;

namespace MyApp.InvoicesApi.Infrastructure;

public static class MessagingExtensions
{
    public static IHostApplicationBuilder UseInvoicesApiMessaging(
        this IHostApplicationBuilder builder)
    {
        builder.UseTransactionalMessaging(msg =>
        {
            msg.UseRabbitMq(TransactionalMessagingOptions.RequireRabbitMq(builder.Configuration));
            msg.AddStore<InvoicesDbContext>(
                TransactionalMessagingOptions.RequireConnectionString(builder.Configuration, "Invoices"),
                MessageStoreRole.Main);
            msg.ListenTo(MessagingQueues.InvoicesOrders, MessagingQueues.OrderEventsExchange);
            msg.IncludeHandler<OrderPlacedIntegrationEventHandler>();
        });
        return builder;
    }
}
