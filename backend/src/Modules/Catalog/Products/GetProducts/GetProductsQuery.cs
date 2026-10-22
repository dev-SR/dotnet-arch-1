using Shared.Common.Pagination;

namespace MyApp.Modules.Catalog.Products.GetProducts;


public sealed record GetProductsQuery(ProductFilter Filter) : IQuery<PagedResponse<ProductListItemDto>>;
