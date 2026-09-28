namespace MyApp.Features.Products.GetProducts;

public class GetProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products", HandleAsync)
            .WithName("GetProducts")
            .WithTags("Products")
            .Produces<List<ProductListItemDto>>(StatusCodes.Status200OK);
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters]
        ProductFilter filter,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductsQuery(filter), ct);
        return result.MatchOk(http);
    }
}
