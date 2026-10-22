using MyApp.Messaging.IntegrationEvents;
using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Persistence;
using Shared.Common.Errors;
using Wolverine.EntityFrameworkCore;

namespace MyApp.Modules.Catalog.Products.CreateProduct;

internal sealed class CreateProductCommandHandler(
    CatalogDbContext dbContext,
    IDbContextOutbox outbox,
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

        // Same scoped DbContext TransactionBehavior will commit.
        outbox.Enroll(dbContext);
        dbContext.Products.Add(product);

        await outbox.PublishAsync(
            new ProductCreatedIntegrationEvent(product.Id, product.Name, product.Price));

        logger.LogInformation("Product {ProductId} queued for create + ProductCreatedIntegrationEvent", product.Id);
        return product.Id;
    }
}
