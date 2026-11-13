using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.OrdersApi.Orders;

public static class OrderTelemetry
{
    public static readonly ActivitySource Source = new("MyApp.Orders", "1.0.0");
    public static readonly Meter Meter = new("MyApp.Orders", "1.0.0");

    public static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("myapp.orders.created");

    public static readonly UpDownCounter<int> ActiveOrders =
        Meter.CreateUpDownCounter<int>("myapp.orders.active");
}
