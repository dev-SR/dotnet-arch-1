using MyApp.Features.Products;

namespace MyApp.Features.Tags;

public sealed class Tag
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    // Skip navigation — EF Core 5+ manages the join table (ProductTag) implicitly,
    // so there's no join entity class to write by hand for a plain many-to-many.
    public ICollection<Product> Products { get; init; } = [];
}
