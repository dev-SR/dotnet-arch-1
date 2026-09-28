using MyApp.Features.Customers;

namespace MyApp.Features.Orders;

public sealed class Order
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; init; } = null!;
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; init; }
    public ICollection<OrderItem> Items { get; init; } = [];
}
