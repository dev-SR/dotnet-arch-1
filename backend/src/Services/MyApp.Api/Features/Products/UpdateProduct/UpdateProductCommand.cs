namespace MyApp.Features.Products.UpdateProduct;

public sealed record UpdateProductCommand(
    Guid Id,
    string Name,
    string Category,
    decimal Price,
    int Stock) : ICommand;
