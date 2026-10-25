namespace MyApp.ShippingApi;

public enum ShipmentStatus
{
    Pending = 0,
    Dispatched = 1,
    Delivered = 2,
}

public sealed class Shipment
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid CustomerId { get; set; }
    public ShipmentStatus Status { get; set; } = ShipmentStatus.Pending;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
