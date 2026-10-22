using MyApp.Modules.Catalog.Products;

namespace MyApp.Modules.Catalog.Categories;

public sealed class Category
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public ICollection<Product> Products { get; init; } = [];
}
