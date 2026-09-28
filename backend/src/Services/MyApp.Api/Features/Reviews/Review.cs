using MyApp.Features.Customers;
using MyApp.Features.Products;

namespace MyApp.Features.Reviews;

public sealed class Review
{
    public Guid Id { get; init; }
    public Guid ProductId { get; set; }
    public Product Product { get; init; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; init; } = null!;
    public int Rating { get; set; }          // 1–5
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; init; }
}
