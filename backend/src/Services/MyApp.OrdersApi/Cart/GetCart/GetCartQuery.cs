using Shared.Application.Abstractions.Queries;

namespace MyApp.OrdersApi.Cart.GetCart;

public sealed record CartLineDto(Guid ProductId, string Name, int Quantity, decimal UnitPrice);

public sealed record CartDto(Guid CustomerId, IReadOnlyList<CartLineDto> Items);

public sealed record GetCartQuery(Guid CustomerId) : IQuery<CartDto>;
