using MyApp.Features.Products;

namespace MyApp.Features.Orders;

public sealed class OrderItem
{
    public Guid Id { get; init; }
    public Guid OrderId { get; set; }
    public Order Order { get; init; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; init; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPriceAtPurchase { get; set; }   // snapshot — the product's price can change later
}
