namespace MyApp.Features.Products.GetProduct;

public class GetProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products/{id:guid}", HandleAsync)
            .WithName("GetProduct")
            .WithTags("Products")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductQuery(id), ct);
        return result.MatchOk(http);
    }
}
