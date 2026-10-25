using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.OrdersApi.Persistence;
using Shared.Common.Errors;
using CartEntity = MyApp.OrdersApi.Cart.Cart;

namespace MyApp.OrdersApi.Cart.AddCartItem;

internal sealed class AddCartItemCommandHandler(OrdersDbContext db)
    : IRequestHandler<AddCartItemCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(
        AddCartItemCommand command,
        CancellationToken cancellationToken)
    {
        var product = await db.ProductCatalog
            .AsNoTracking()
            .SingleOrDefaultAsync(p => p.ProductId == command.ProductId, cancellationToken);

        if (product is null)
            return Error.NotFound("PRODUCT.NOT_SYNCED", "Product is not in the orders catalog yet.");

        var cart = await db.Carts
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.CustomerId == command.CustomerId, cancellationToken);

        if (cart is null)
        {
            cart = new CartEntity { CustomerId = command.CustomerId };
            db.Carts.Add(cart);
        }

        var existing = cart.Items.SingleOrDefault(i => i.ProductId == command.ProductId);
        if (existing is null)
        {
            cart.Items.Add(new CartItem
            {
                CustomerId = command.CustomerId,
                ProductId = command.ProductId,
                Quantity = command.Quantity,
            });
        }
        else
        {
            existing.Quantity = command.Quantity;
        }

        return default(Success);
    }
}

