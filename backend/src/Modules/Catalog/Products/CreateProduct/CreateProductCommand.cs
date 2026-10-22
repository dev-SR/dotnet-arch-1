// Features/Products/CreateProduct/CreateProductCommand.cs

namespace MyApp.Modules.Catalog.Products.CreateProduct;

public sealed record CreateProductCommand(
    string Name,
    string Category,
    decimal Price,
    int Stock) : ICommand<Guid>;
