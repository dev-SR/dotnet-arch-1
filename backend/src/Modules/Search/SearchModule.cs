using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyApp.Messaging;
using MyApp.Modules.Search.Consumers.IndexProduct;

namespace MyApp.Modules.Search;

public static class SearchModule
{
    /// <summary>Listen for product events and index into Elasticsearch.</summary>
    public static void ConfigureMessaging(TransactionalMessagingOptions msg)
    {
        msg.ListenTo(MessagingQueues.SearchProducts, MessagingQueues.ProductEventsExchange);
        msg.IncludeHandler<ProductCreatedIntegrationEventHandler>();
        msg.IncludeHandler<ProductUpdatedIntegrationEventHandler>();
    }

    public static WebApplication UseSearchModule(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var es = scope.ServiceProvider.GetRequiredService<ElasticsearchClient>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("SearchModule");

        try
        {
            EnsureProductsIndexAsync(es).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            // Testing / local: allow ApiHost to start without ES so auth/catalog tests can run
            // when only Rabbit (or nothing) is up. Search handlers/API return errors until ES is healthy.
            if (app.Environment.IsEnvironment("Testing"))
            {
                logger.LogWarning(ex, "Elasticsearch unavailable — search index not ready");
                return app;
            }

            logger.LogError(ex, "Elasticsearch products index bootstrap failed");
            throw;
        }

        return app;
    }

    private static async Task EnsureProductsIndexAsync(ElasticsearchClient es)
    {
        var exists = await es.Indices.ExistsAsync(SearchIndexNames.Products);
        if (exists.Exists)
            return;

        var create = await es.Indices.CreateAsync(SearchIndexNames.Products, c => c
            .Mappings(m => m
                .Properties<ProductSearchDocument>(p => p
                    .Keyword(k => k.ProductId)
                    .Text(t => t.Name)
                    .DoubleNumber(n => n.Price)
                    .Date(d => d.IndexedAt))));

        if (!create.IsValidResponse)
            throw new InvalidOperationException(
                create.ElasticsearchServerError?.Error.Reason
                ?? create.DebugInformation
                ?? "Failed to create products index.");
    }
}
