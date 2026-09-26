using MyApp.Persistence;
using Shared.Common.Errors;

namespace MyApp.Features.Products.CreateProduct;

internal sealed class CreateProductCommandHandler(
    AppDbContext dbContext,
    ILogger<CreateProductCommandHandler> logger)
    : IRequestHandler<CreateProductCommand, ErrorOr<Guid>>
{
    public ValueTask<ErrorOr<Guid>> Handle(
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

        // SaveChanges is handled by TransactionBehavior
        logger.LogInformation("Product {ProductId} created", product.Id);
        return ValueTask.FromResult<ErrorOr<Guid>>(product.Id);
    }
}
