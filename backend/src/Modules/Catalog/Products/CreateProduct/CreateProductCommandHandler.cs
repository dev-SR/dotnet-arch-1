using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Persistence;
using Shared.Common.Errors;

namespace MyApp.Modules.Catalog.Products.CreateProduct;

internal sealed class CreateProductCommandHandler(
    CatalogDbContext dbContext,
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
            Price = command.Price,
            Stock = command.Stock,
            CreatedAt = DateTime.UtcNow,
            Category = new Category { Name = command.Category },
            Brand = new Brand { Name = "Generic" },
        };
        dbContext.Products.Add(product);

        // SaveChanges is handled by TransactionBehavior
        logger.LogInformation("Product {ProductId} created", product.Id);
        return ValueTask.FromResult<ErrorOr<Guid>>(product.Id);
    }
}
