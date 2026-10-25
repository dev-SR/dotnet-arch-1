namespace MyApp.OrdersApi.Cart;

public sealed class CartItem
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid CustomerId { get; set; }
    public Cart Cart { get; init; } = null!;
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}
