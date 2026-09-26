namespace MyApp.Features.Products.CreateProduct;

public class CreateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/products", HandleAsync)
            .WithName("CreateProduct")
            .WithTags("Products");
    }

    private static async Task<IResult> HandleAsync(
        CreateProductCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.MatchCreated(http, id => $"/products/{id}");
    }
}
