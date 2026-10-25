namespace MyApp.OrdersApi.Cart;

public sealed class Cart
{
    public Guid CustomerId { get; set; }
    public ICollection<CartItem> Items { get; init; } = [];
}
