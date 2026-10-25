using Shared.Application.Abstractions.Queries;

namespace MyApp.Modules.Search.SearchProducts;

public sealed record ProductSearchHit(Guid ProductId, string Name, decimal Price);

public sealed record SearchProductsQuery(string? Q) : IQuery<IReadOnlyList<ProductSearchHit>>;
