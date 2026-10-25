namespace MyApp.Messaging;

public static class MessagingQueues
{
    public const string ProductEventsExchange = "product-events";
    public const string SearchProducts = "search.product-events";
    public const string OrdersProducts = "orders.product-events";

    public const string OrderEventsExchange = "order-events";
    public const string InvoicesOrders = "invoices.order-events";
    public const string ShippingOrders = "shipping.order-events";
}
