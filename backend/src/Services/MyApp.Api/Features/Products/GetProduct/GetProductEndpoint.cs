using Microsoft.AspNetCore.Http.HttpResults;

namespace MyApp.Features.Products.GetProduct;


public class GetProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products/{id:guid}", HandleAsync)
            .WithName("GetProduct")
            .WithTags("Products");
    }

    private async Task<Results<Ok<ProductDto>, NotFound>> HandleAsync(
        Guid id,
        IMediator mediator,
        CancellationToken ct)
    {
        var product = await mediator.Send(new GetProductQuery(id), ct);
        return product is null ? TypedResults.NotFound() : TypedResults.Ok(product);
    }
}
