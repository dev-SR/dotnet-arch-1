namespace MyApp.Modules.Orders.Catalog;

/// <summary>Local read model of Catalog products (fed by integration events).</summary>
public sealed class ProductCatalogItem
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}
