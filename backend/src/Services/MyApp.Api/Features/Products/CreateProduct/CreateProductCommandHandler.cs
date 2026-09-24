// Features/Products/CreateProduct/CreateProductCommandHandler.cs

using MyApp.Persistence;

namespace MyApp.Features.Products.CreateProduct;

internal sealed class CreateProductCommandHandler(
    AppDbContext dbContext,
    ILogger<CreateProductCommandHandler> logger)
    : IRequestHandler<CreateProductCommand, Guid>
{
    public ValueTask<Guid> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
    {
        try
        {
            var product = new Product
            {
                Id = Guid.NewGuid(),
                Name = command.Name,
                Category = command.Category,
                Price = command.Price,
                Stock = command.Stock,
                CreatedAt = DateTime.UtcNow
            };
            dbContext.Products.Add(product);

            // SaveChanges is now handled directly inside the command handler
            // await dbContext.SaveChangesAsync(cancellationToken);
            // SaveChanges is handled by TransactionBehavior
            // No need to call it here
            logger.LogInformation("Product {ProductId} created", product.Id);
            return ValueTask.FromResult(product.Id);
        }
        catch (Exception exception)
        {
            return ValueTask.FromException<Guid>(exception);
        }
    }
}
