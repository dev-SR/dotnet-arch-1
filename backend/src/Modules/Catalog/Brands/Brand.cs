using MyApp.Modules.Catalog.Products;

namespace MyApp.Modules.Catalog.Brands;

public sealed class Brand
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string? Country { get; set; }
    public ICollection<Product> Products { get; init; } = [];
}
