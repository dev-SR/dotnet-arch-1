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
        var created = Product.Create(
            command.Name,
            command.Price,
            command.Stock,
            new Category { Name = command.Category },
            new Brand { Name = "Generic" });

        if (created.IsError)
            return ValueTask.FromResult(ErrorOr<Guid>.From(created.Errors.ToList()));

        var product = created.Value;
        dbContext.Products.Add(product);

        // SaveChanges is handled by TransactionBehavior (audit + domain events via interceptors)
        logger.LogInformation("Product {ProductId} queued for create", product.Id);
        return ValueTask.FromResult<ErrorOr<Guid>>(product.Id);
    }
}
