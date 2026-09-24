// Features/Products/GetProduct/GetProductQuery.cs

using Shared.Application.Behaviors;

namespace MyApp.Features.Products.GetProduct;

public sealed record GetProductQuery(Guid Id) : IRequest<ProductDto?>, ICacheable
{
    public string CacheKey => $"product:{Id}";
    public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
}
