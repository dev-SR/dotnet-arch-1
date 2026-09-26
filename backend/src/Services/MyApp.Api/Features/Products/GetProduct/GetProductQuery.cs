namespace MyApp.Features.Products.GetProduct;

public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>, ICacheable
{
    public string CacheKey => $"product:{Id}";
    public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
}
