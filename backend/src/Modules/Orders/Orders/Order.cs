using Shared.Domain;

namespace MyApp.Modules.Orders;

public sealed class Order : Aggregate
{
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public ICollection<OrderItem> Items { get; init; } = [];
}
