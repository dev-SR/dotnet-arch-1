using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Shared.Common.Errors;

namespace MyApp.Modules.Search.SearchProducts;

internal sealed class SearchProductsQueryHandler(ElasticsearchClient es)
    : IRequestHandler<SearchProductsQuery, ErrorOr<IReadOnlyList<ProductSearchHit>>>
{
    public async ValueTask<ErrorOr<IReadOnlyList<ProductSearchHit>>> Handle(
        SearchProductsQuery query,
        CancellationToken cancellationToken)
    {
        var esQuery = string.IsNullOrWhiteSpace(query.Q)
            ? (Query)new MatchAllQuery()
            : new MatchQuery { Field = "name", Query = query.Q };

        var response = await es.SearchAsync<ProductSearchDocument>(s => s
            .Indices(SearchIndexNames.Products)
            .Size(20)
            .Query(esQuery), cancellationToken);

        if (!response.IsValidResponse)
        {
            var detail = response.ElasticsearchServerError?.Error.Reason
                         ?? response.DebugInformation
                         ?? "Elasticsearch request failed.";
            return Error.ServiceUnavailable(detail, "SEARCH.UNAVAILABLE");
        }

        var hits = response.Hits
            .Select(h => h.Source)
            .Where(d => d is not null)
            .Select(d => new ProductSearchHit(d!.ProductId, d.Name, d.Price))
            .ToList();

        return hits;
    }
}
