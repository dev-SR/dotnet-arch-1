using MyApp.Persistence;

namespace MyApp.Features.Products.GetProducts;

// Features/Products/GetProducts/GetProductsQueryHandler.cs
using Microsoft.EntityFrameworkCore;


internal sealed class GetProductsQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductsQuery, List<ProductDto>>
{
    public async ValueTask<List<ProductDto>> Handle(
        GetProductsQuery query,
        CancellationToken cancellationToken)
    {
        return await dbContext.Products
            .AsNoTracking()
            .OrderByDescending(p => p.CreatedAt) // Newest first
            .Select(p => new ProductDto(
                p.Id,
                p.Name,
                p.Category,
                p.Price,
                p.Stock))
            .ToListAsync(cancellationToken);
    }
}
