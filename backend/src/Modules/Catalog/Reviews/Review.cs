using MyApp.Modules.Catalog.Products;

namespace MyApp.Modules.Catalog.Reviews;

public sealed class Review
{
    public Guid Id { get; init; }
    public Guid ProductId { get; set; }
    public Product Product { get; init; } = null!;
    public Guid CustomerId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; init; }
}
