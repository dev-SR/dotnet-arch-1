// Features/Products/GetProduct/GetProductQuery.cs

namespace MyApp.Features.Products.GetProduct;

public sealed record GetProductQuery(Guid Id) : IRequest<ProductDto?>;
