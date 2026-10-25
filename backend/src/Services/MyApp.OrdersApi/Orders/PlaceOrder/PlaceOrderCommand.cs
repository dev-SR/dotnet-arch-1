using Shared.Application.Abstractions.Commands;
using Shared.Common.Errors;

namespace MyApp.OrdersApi.Orders.PlaceOrder;

public sealed record PlaceOrderCommand(Guid CustomerId) : ICommand<Guid>;
