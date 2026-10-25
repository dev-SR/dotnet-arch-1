using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.OrdersApi.Persistence;
using Shared.Common.Errors;

namespace MyApp.OrdersApi.Cart.RemoveCartItem;

internal sealed class RemoveCartItemCommandHandler(OrdersDbContext db)
    : IRequestHandler<RemoveCartItemCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(
        RemoveCartItemCommand command,
        CancellationToken cancellationToken)
    {
        var cart = await db.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.CustomerId == command.CustomerId, cancellationToken);

        if (cart is null)
            return Error.NotFound("CART.NOT_FOUND", "Cart not found.");

        var item = cart.Items.SingleOrDefault(i => i.ProductId == command.ProductId);
        if (item is null)
            return Error.NotFound("CART.ITEM_NOT_FOUND", "Item not in cart.");

        cart.Items.Remove(item);
        db.CartItems.Remove(item);
        return default(Success);
    }
}
