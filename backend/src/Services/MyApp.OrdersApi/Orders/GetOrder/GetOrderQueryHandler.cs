using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.OrdersApi.Persistence;
using Shared.Common.Errors;

namespace MyApp.OrdersApi.Orders.GetOrder;

internal sealed class GetOrderQueryHandler(OrdersDbContext db)
    : IRequestHandler<GetOrderQuery, ErrorOr<OrderDto>>
{
    public async ValueTask<ErrorOr<OrderDto>> Handle(
        GetOrderQuery query,
        CancellationToken cancellationToken)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .SingleOrDefaultAsync(o => o.Id == query.OrderId, cancellationToken);

        if (order is null)
            return Error.NotFound("ORDER.NOT_FOUND", $"Order '{query.OrderId}' was not found.");

        if (order.CustomerId != query.RequesterId && !query.IsAdmin)
            return Error.Forbidden("ORDER.FORBIDDEN", "You are not allowed to view this order.");

        return new OrderDto(
            order.Id,
            order.CustomerId,
            order.Status.ToString(),
            order.Total,
            order.Items.Select(i => new OrderLineDto(
                i.ProductId,
                i.ProductName,
                i.Quantity,
                i.UnitPriceAtPurchase)).ToList());
    }
}
