namespace MyApp.OrdersApi.Catalog;

public sealed class ProductCatalogItem
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
