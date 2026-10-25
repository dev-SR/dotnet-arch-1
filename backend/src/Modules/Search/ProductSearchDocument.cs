namespace MyApp.Modules.Search;

public sealed class ProductSearchDocument
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public DateTimeOffset IndexedAt { get; set; }
}
