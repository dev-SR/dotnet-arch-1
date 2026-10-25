using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.OrdersApi.Persistence;
using Shared.Common.Errors;

namespace MyApp.OrdersApi.Cart.GetCart;

internal sealed class GetCartQueryHandler(OrdersDbContext db)
    : IRequestHandler<GetCartQuery, ErrorOr<CartDto>>
{
    public async ValueTask<ErrorOr<CartDto>> Handle(
        GetCartQuery query,
        CancellationToken cancellationToken)
    {
        var entity = await db.Carts.AsNoTracking()
            .Include(c => c.Items)
            .SingleOrDefaultAsync(c => c.CustomerId == query.CustomerId, cancellationToken);

        if (entity is null)
            return new CartDto(query.CustomerId, []);

        var productIds = entity.Items.Select(i => i.ProductId).ToList();
        var products = await db.ProductCatalog.AsNoTracking()
            .Where(p => productIds.Contains(p.ProductId))
            .ToDictionaryAsync(p => p.ProductId, cancellationToken);

        var lines = entity.Items.Select(i =>
        {
            products.TryGetValue(i.ProductId, out var p);
            return new CartLineDto(i.ProductId, p?.Name ?? "(unknown)", i.Quantity, p?.Price ?? 0);
        }).ToList();

        return new CartDto(query.CustomerId, lines);
    }
}
