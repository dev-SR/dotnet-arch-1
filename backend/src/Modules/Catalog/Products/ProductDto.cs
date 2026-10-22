namespace MyApp.Modules.Catalog.Products;

public sealed record ProductDto(
    Guid Id,
    string Name,
    string Category,
    decimal Price,
    int Stock);
