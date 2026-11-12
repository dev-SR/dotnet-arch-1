using MyApp.OrdersApi.Orders.PlaceOrder.Events;
using Shared.Common.Errors;
using Shared.Domain;

namespace MyApp.OrdersApi.Orders;

public sealed class Order : Aggregate
{
    private Order() { }   // EF

    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public decimal Total { get; private set; }
    public ICollection<OrderItem> Items { get; private set; } = [];

    /// <summary>
    /// Places a new order from priced lines. Owns the invariants: at least one line, positive quantities,
    /// non-negative prices. Total is derived from the lines. Raises <see cref="OrderPlacedDomainEvent"/>.
    /// </summary>
    public static ErrorOr<Order> Place(Guid customerId, IReadOnlyList<OrderLine> lines)
    {
        var errors = new List<Error>();
        if (customerId == Guid.Empty)
            errors.Add(Error.Validation("ORDER.CUSTOMER", "Customer is required."));
        if (lines.Count == 0)
            errors.Add(Error.Validation("ORDER.EMPTY", "An order needs at least one line."));
        foreach (var line in lines)
        {
            if (line.Quantity <= 0)
                errors.Add(Error.Validation("ORDER.QUANTITY", "Quantity must be greater than zero."));
            if (line.UnitPrice < 0)
                errors.Add(Error.Validation("ORDER.PRICE", "Unit price cannot be negative."));
        }

        if (errors.Count > 0)
            return errors;

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Placed,
        };

        foreach (var line in lines)
            order.Items.Add(new OrderItem(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice));

        order.Total = order.Items.Sum(i => i.LineTotal);
        order.AddDomainEvent(new OrderPlacedDomainEvent(
            order.Id,
            order.CustomerId,
            order.Total,
            order.Items.Select(i => new OrderLineSnapshot(
                i.ProductId, i.ProductName, i.Quantity, i.UnitPriceAtPurchase)).ToList()));

        return order;
    }
}
