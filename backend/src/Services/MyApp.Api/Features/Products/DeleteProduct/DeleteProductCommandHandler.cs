using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;

namespace MyApp.Features.Products.DeleteProduct;

internal sealed class DeleteProductCommandHandler(
    AppDbContext dbContext,
    ILogger<DeleteProductCommandHandler> logger)
    : IRequestHandler<DeleteProductCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(
        DeleteProductCommand command,
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

        dbContext.Products.Remove(product);
        logger.LogInformation("Product {ProductId} deleted", product.Id);
        return default(Success);
    }
}
