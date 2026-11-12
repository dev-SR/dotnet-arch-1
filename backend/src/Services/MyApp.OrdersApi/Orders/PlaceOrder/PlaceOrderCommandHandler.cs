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

        var lines = cart.Items
            .Select(item =>
            {
                var product = products[item.ProductId];
                return new OrderLine(item.ProductId, product.Name, item.Quantity, product.Price);
            })
            .ToList();

        var placed = Order.Place(command.CustomerId, lines);
        if (placed.IsError)
            return placed.Errors.ToList();

        db.Orders.Add(placed.Value);
        db.CartItems.RemoveRange(cart.Items);
        db.Carts.Remove(cart);

        return placed.Value.Id;
    }
}
