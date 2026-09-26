using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;

namespace MyApp.Features.Products.UpdateProduct;

internal sealed class UpdateProductCommandHandler(
    AppDbContext dbContext,
    ILogger<UpdateProductCommandHandler> logger)
    : IRequestHandler<UpdateProductCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(
        UpdateProductCommand command,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .FirstOrDefaultAsync(p => p.Id == command.Id, cancellationToken);

        if (product is null)
        {
            return Error.NotFound(
                "PRODUCT.NOT_FOUND",
                $"Product '{command.Id}' was not found.");
        }

        product.Name = command.Name;
        product.Category = command.Category;
        product.Price = command.Price;
        product.Stock = command.Stock;

        logger.LogInformation("Product {ProductId} updated", product.Id);
        return default(Success);
    }
}
