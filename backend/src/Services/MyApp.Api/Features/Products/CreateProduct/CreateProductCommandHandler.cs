// Features/Products/CreateProduct/CreateProductCommandHandler.cs

using MyApp.Persistence;

namespace MyApp.Features.Products.CreateProduct;

internal sealed class CreateProductCommandHandler(
    AppDbContext dbContext,
    ILogger<CreateProductCommandHandler> logger)
    : IRequestHandler<CreateProductCommand, Guid>
{
    public async ValueTask<Guid> Handle(
        CreateProductCommand command,
        CancellationToken cancellationToken)
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
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Product {ProductId} created", product.Id);
        return product.Id;
    }
}
