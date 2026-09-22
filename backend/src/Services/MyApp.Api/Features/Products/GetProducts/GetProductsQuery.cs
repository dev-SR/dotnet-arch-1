namespace MyApp.Features.Products.GetProducts;


public sealed record GetProductsQuery() : IRequest<List<ProductDto>>;
