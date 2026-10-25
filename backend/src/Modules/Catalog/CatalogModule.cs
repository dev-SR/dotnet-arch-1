using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyApp.Messaging;
using MyApp.Messaging.IntegrationEvents;
using MyApp.Modular;
using MyApp.Modules.Catalog.Persistence;
using Wolverine.Persistence.Durability;

namespace MyApp.Modules.Catalog;

public static class CatalogModule
{
    public static WebApplication UseCatalogModule(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<CatalogDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogModule");

        try
        {
            DbContextSchema.Apply(db);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog database migrate failed");
            throw;
        }

        return app;
    }

    /// <summary>Publish product integration events from this module's store.</summary>
    public static void ConfigureMessaging(
        TransactionalMessagingOptions msg,
        IConfiguration configuration)
    {
        msg.AddStore<CatalogDbContext>(
            TransactionalMessagingOptions.RequireConnectionString(configuration, "Catalog"),
            MessageStoreRole.Main);
        msg.PublishToExchange<ProductCreatedIntegrationEvent>(MessagingQueues.ProductEventsExchange);
        msg.PublishToExchange<ProductUpdatedIntegrationEvent>(MessagingQueues.ProductEventsExchange);
    }
}
