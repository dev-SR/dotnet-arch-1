namespace MyApp.Features.Products;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Category,
    decimal Price,
    int Stock);
