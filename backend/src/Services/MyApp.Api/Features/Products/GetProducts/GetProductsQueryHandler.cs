using MyApp.Features.Products.Specifications;
using MyApp.Persistence;
using Shared.Common.Pagination;
using Shared.Common.Specifications;

namespace MyApp.Features.Products.GetProducts;

internal sealed class GetProductsQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductsQuery, ErrorOr<PagedResponse<ProductListItemDto>>>
{
    public async ValueTask<ErrorOr<PagedResponse<ProductListItemDto>>> Handle(
        GetProductsQuery query, CancellationToken ct)
    {
        var pagedFilter = query.Filter;

        // 1–2. Identical to §2.8 (compose specs → Apply → Select).
        var spec = ActiveProductSpec.Instance;
        if (pagedFilter.InStockOnly is not null) spec = spec.And(new InStockSpecification());
        if (pagedFilter.CategoryId is { } categoryId) spec = spec.And(new ByCategorySpecification(categoryId));
        if (pagedFilter.BrandId is { } brandId) spec = spec.And(new ByBrandSpecification(brandId));
        if (pagedFilter.TagId is { } tagId) spec = spec.And(new ByTagSpecification(tagId));
        if (pagedFilter.MinPrice is not null || pagedFilter.MaxPrice is not null)
            spec = spec.And(new PriceRangeSpecification(pagedFilter.MinPrice, pagedFilter.MaxPrice));
        if (!string.IsNullOrWhiteSpace(pagedFilter.Search))
            spec = spec.And(new ProductSearchSpecification(pagedFilter.Search));

        var projected = dbContext.Products.Apply(spec)
            .Select(p => new ProductListItemDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price,
                StockQuantity = p.StockQuantity,
                CategoryName = p.Category.Name,
                BrandName = p.Brand.Name,
                CreatedAt = p.CreatedAt,
            });
        var page = await projected.ToPagedAsync(pagedFilter, ct);
        return new PagedResponse<ProductListItemDto>(page.Items, page.TotalCount, page.Page, page.PageSize);
    }
}
