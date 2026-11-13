using System.Diagnostics;
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
        using var activity = CatalogTelemetry.Source.StartActivity("CreateProduct");
        activity?.SetTag("myapp.module", "catalog");
        activity?.SetTag("myapp.slice", "create_product");
        var started = Stopwatch.GetTimestamp();

        var created = Product.Create(
            command.Name,
            command.Price,
            command.Stock,
            new Category { Name = command.Category },
            new Brand { Name = "Generic" });

        if (created.IsError)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Product.Create returned errors");
            return ErrorOr<Guid>.From(created.Errors.ToList());
        }

        var product = created.Value;
        dbContext.Products.Add(product);

        activity?.AddEvent(new ActivityEvent("domain.event.raised", DateTimeOffset.UtcNow,
            new ActivityTagsCollection { { "event.type", "ProductCreated" } }));
        CatalogTelemetry.ProductsCreated.Add(1,
            new KeyValuePair<string, object?>("category", command.Category));
        CatalogTelemetry.CreateDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        activity?.SetStatus(ActivityStatusCode.Ok);

        logger.LogInformation("Product {ProductId} created", product.Id);
        return product.Id;
    }
}
