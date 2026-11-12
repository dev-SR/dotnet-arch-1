namespace MyApp.OrdersApi.Orders;

/// <summary>One priced line of an order, as the caller supplies it to <see cref="Order.Place"/>.</summary>
public sealed record OrderLine(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice);
