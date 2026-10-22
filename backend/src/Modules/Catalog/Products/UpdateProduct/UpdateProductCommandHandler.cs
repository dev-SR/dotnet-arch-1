using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Catalog.Persistence;

namespace MyApp.Modules.Catalog.Products.UpdateProduct;

internal sealed class UpdateProductCommandHandler(
    CatalogDbContext dbContext,
    ILogger<UpdateProductCommandHandler> logger)
    : IRequestHandler<UpdateProductCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.Include(product => product.Category)
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (product is null)
        {
            return Error.NotFound(
                "PRODUCT.NOT_FOUND",
                $"Product '{command.Id}' was not found.");
        }

        var updated = product.UpdateDetails(command.Name, command.Price, command.Stock, command.Category);
        if (updated.IsError)
            return ErrorOr<Success>.From(updated.Errors.ToList());

        logger.LogInformation("Product {ProductId} updated", product.Id);
        return default(Success);
    }
}
