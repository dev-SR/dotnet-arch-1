using Shared.Common.Pagination;

namespace MyApp.Features.Products.GetProducts;


public sealed record GetProductsQuery(ProductFilter Filter) : IQuery<PagedResponse<ProductListItemDto>>;
