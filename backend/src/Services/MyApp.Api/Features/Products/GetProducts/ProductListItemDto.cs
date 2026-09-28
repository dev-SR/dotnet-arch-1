namespace MyApp.Features.Products.GetProducts;

public sealed class ProductListItemDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    public decimal Price { get; init; }
    public int StockQuantity { get; init; }
    public string CategoryName { get; init; } = "";
    public string BrandName { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}
