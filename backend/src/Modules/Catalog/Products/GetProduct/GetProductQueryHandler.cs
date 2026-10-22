using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Catalog.Persistence;
using Shared.Common.Errors;

namespace MyApp.Modules.Catalog.Products.GetProduct;

internal sealed class GetProductQueryHandler(
    CatalogDbContext dbContext)
    : IRequestHandler<GetProductQuery, ErrorOr<ProductDto>>
{
    public async ValueTask<ErrorOr<ProductDto>> Handle(
        GetProductQuery query,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking().Include(product => product.Category)
            .FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken);

        if (product is null)
        {
            return Error.NotFound(
                "PRODUCT.NOT_FOUND",
                $"Product '{query.Id}' was not found.");
        }

        return new ProductDto(
            product.Id,
            product.Name,
            Category: product.Category.Name,
            product.Price,
            product.Stock
        );
    }
}
