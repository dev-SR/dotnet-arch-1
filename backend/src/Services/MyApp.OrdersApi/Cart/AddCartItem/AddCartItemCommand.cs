using Shared.Application.Abstractions.Commands;
using Shared.Common.Errors;

namespace MyApp.OrdersApi.Cart.AddCartItem;

public sealed record AddCartItemCommand(Guid CustomerId, Guid ProductId, int Quantity) : ICommand;
