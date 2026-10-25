using MyApp.OrdersApi.Orders.PlaceOrder.Events;
using Shared.Domain;

namespace MyApp.OrdersApi.Orders;

public sealed class Order : Aggregate
{
    public Guid CustomerId { get; set; }
    public OrderStatus Status { get; set; }
    public decimal Total { get; set; }
    public ICollection<OrderItem> Items { get; init; } = [];

    /// <summary>Marks the order as placed and raises <see cref="OrderPlacedDomainEvent"/>.</summary>
    public void ConfirmPlaced()
    {
        Status = OrderStatus.Placed;
        AddDomainEvent(new OrderPlacedDomainEvent(
            Id,
            CustomerId,
            Total,
            Items.Select(i => new OrderLineSnapshot(
                i.ProductId, i.ProductName, i.Quantity, i.UnitPriceAtPurchase)).ToList()));
    }
}
