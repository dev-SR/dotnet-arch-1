using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Reviews;
using MyApp.Modules.Catalog.Tags;

namespace MyApp.Modules.Catalog.Products;

public sealed class Product
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }

    public int Stock { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; init; }

    public Guid CategoryId { get; set; }
    public Category Category { get; init; } = null!;

    public Guid BrandId { get; set; }
    public Brand Brand { get; init; } = null!;

    public ICollection<Tag> Tags { get; set; } = [];
    public ICollection<Review> Reviews { get; init; } = [];
}
