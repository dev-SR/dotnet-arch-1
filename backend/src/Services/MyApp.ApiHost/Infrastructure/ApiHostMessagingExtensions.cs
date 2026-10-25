using MyApp.Messaging;
using MyApp.Modules.Catalog;
using MyApp.Modules.Search;

namespace MyApp.Infrastructure;

public static class ApiHostMessagingExtensions
{
    public static IHostApplicationBuilder UseApiHostMessaging(
        this IHostApplicationBuilder builder)
    {
        builder.UseTransactionalMessaging(msg =>
        {
            msg.UseRabbitMq(TransactionalMessagingOptions.RequireRabbitMq(builder.Configuration));
            CatalogModule.ConfigureMessaging(msg, builder.Configuration);
            SearchModule.ConfigureMessaging(msg);
        });
        return builder;
    }
}
