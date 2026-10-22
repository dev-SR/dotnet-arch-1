namespace MyApp.Modules.Orders;

public sealed class OrderItem
{
    public Guid Id { get; init; }
    public Guid OrderId { get; set; }
    public Order Order { get; init; } = null!;
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
    public decimal UnitPriceAtPurchase { get; set; }
}
