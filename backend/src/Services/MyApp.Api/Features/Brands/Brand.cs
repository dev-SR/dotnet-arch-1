using MyApp.Features.Products;

namespace MyApp.Features.Brands;

public sealed class Brand
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string? Country { get; set; }
    public ICollection<Product> Products { get; init; } = [];
}
