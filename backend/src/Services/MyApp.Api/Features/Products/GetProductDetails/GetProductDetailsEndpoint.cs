
namespace MyApp.Features.Products.GetProductDetails;

public sealed class GetProductDetailsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products/{id:guid}/details", HandleAsync)
            .WithName("GetProductDetails")
            .WithTags("Products")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductDetailsQuery(id), ct);
        return result.MatchOk(http);
    }
}
