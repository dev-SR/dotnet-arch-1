namespace MyApp.OrdersApi.Orders;

public sealed class OrderItem
{
    private OrderItem() { }   // EF

    internal OrderItem(Guid productId, string productName, int quantity, decimal unitPriceAtPurchase)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPriceAtPurchase = unitPriceAtPurchase;
    }

    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Order Order { get; private set; } = null!;
    public Guid ProductId { get; private set; }
    public string ProductName { get; private set; } = "";
    public int Quantity { get; private set; }
    public decimal UnitPriceAtPurchase { get; private set; }

    public decimal LineTotal => UnitPriceAtPurchase * Quantity;
}
