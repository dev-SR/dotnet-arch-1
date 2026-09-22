// Features/Products/GetProduct/GetProductQueryHandler.
using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;

namespace MyApp.Features.Products.GetProduct;

internal sealed class GetProductQueryHandler(
    AppDbContext dbContext)
    : IRequestHandler<GetProductQuery, ProductDto?>
{
    public async ValueTask<ProductDto?> Handle(
        GetProductQuery query,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken);

        return product is null
            ? null
            : new ProductDto(
                product.Id,
                product.Name,
                product.Category,
                product.Price,
                product.Stock);
    }
}
