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
    public async ValueTask<ErrorOr<Guid>> Handle(
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
            return ErrorOr<Guid>.From(created.Errors.ToList());

        var product = created.Value;
        dbContext.Products.Add(product);

        logger.LogInformation("Product {ProductId} created", product.Id);
        return product.Id;
    }
}
