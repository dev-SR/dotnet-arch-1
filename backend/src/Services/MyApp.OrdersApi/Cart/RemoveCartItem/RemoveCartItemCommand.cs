using Shared.Application.Abstractions.Commands;
using Shared.Common.Errors;

namespace MyApp.OrdersApi.Cart.RemoveCartItem;

public sealed record RemoveCartItemCommand(Guid CustomerId, Guid ProductId) : ICommand;
