using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using MyApp.Messaging.IntegrationEvents;

namespace MyApp.Modules.Search.Consumers.IndexProduct;

public sealed class ProductCreatedIntegrationEventHandler
{
    public async Task Handle(
        ProductCreatedIntegrationEvent message,
        ElasticsearchClient es,
        CancellationToken ct)
    {
        var doc = new ProductSearchDocument
        {
            ProductId = message.ProductId,
            Name = message.Name,
            Price = message.Price,
            IndexedAt = DateTimeOffset.UtcNow,
        };

        await es.IndexAsync(doc, i => i
            .Index(SearchIndexNames.Products)
            .Id(message.ProductId.ToString())
            .Refresh(Refresh.WaitFor), ct);
    }
}
