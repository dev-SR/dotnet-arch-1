// Features/Products/CreateProduct/CreateProductCommand.cs

namespace MyApp.Features.Products.CreateProduct;

public sealed record CreateProductCommand(
    string Name,
    string Category,
    decimal Price,
    int Stock) : ICommand<Guid>;
