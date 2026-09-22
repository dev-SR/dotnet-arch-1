
using Microsoft.AspNetCore.Http.HttpResults;

namespace MyApp.Features.Products.GetProducts;

// Features/Products/GetProducts/GetProductsEndpoint.cs

public class GetProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products", HandleAsync)
            .WithName("GetProducts")
            .WithTags("Products")
            .Produces<List<ProductDto>>(StatusCodes.Status200OK);
    }

    private async Task<Ok<List<ProductDto>>> HandleAsync(
        IMediator mediator,
        CancellationToken ct)
    {
        var products = await mediator.Send(new GetProductsQuery(), ct);
        return TypedResults.Ok(products);
    }
}
