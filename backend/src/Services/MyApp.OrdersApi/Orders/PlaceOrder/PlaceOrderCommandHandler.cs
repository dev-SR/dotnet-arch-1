using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.OrdersApi.Persistence;
using Shared.Common.Errors;

namespace MyApp.OrdersApi.Orders.PlaceOrder;

internal sealed class PlaceOrderCommandHandler(OrdersDbContext db)
    : IRequestHandler<PlaceOrderCommand, ErrorOr<Guid>>
{
    public async ValueTask<ErrorOr<Guid>> Handle(
        PlaceOrderCommand command,
        CancellationToken cancellationToken)
    {
        var cart = await db.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.CustomerId == command.CustomerId, cancellationToken);

        if (cart is null || cart.Items.Count == 0)
            return Error.Validation("CART.EMPTY", "Cart is empty.");

        var productIds = cart.Items.Select(i => i.ProductId).ToList();
        var products = await db.ProductCatalog
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, cancellationToken);

        var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            return Error.Validation("CART.UNKNOWN_PRODUCT", "Cart contains products not in the local catalog.");

        var order = new Order
        {
            CustomerId = command.CustomerId,
            Status = OrderStatus.Placed,
        };

        foreach (var item in cart.Items)
        {
            var product = products[item.ProductId];
            order.Items.Add(new OrderItem
            {
                ProductId = item.ProductId,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitPriceAtPurchase = product.Price,
            });
        }

        order.Total = order.Items.Sum(i => i.UnitPriceAtPurchase * i.Quantity);
        order.ConfirmPlaced();

        db.Orders.Add(order);
        db.CartItems.RemoveRange(cart.Items);
        db.Carts.Remove(cart);

        return order.Id;
    }
}
