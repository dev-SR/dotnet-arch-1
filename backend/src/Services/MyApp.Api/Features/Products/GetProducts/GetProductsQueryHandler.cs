using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;
using Shared.Common.Errors;

namespace MyApp.Features.Products.GetProducts;

internal sealed class GetProductsQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductsQuery, ErrorOr<List<ProductDto>>>
{
    public async ValueTask<ErrorOr<List<ProductDto>>> Handle(
        GetProductsQuery query,
        CancellationToken cancellationToken)
    {
        var products = await dbContext.Products
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Category,
                p.Price,
                p.Stock))
            .ToListAsync(cancellationToken);

        return products;
    }
}
