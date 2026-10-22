using MyApp.Auth.Jwt;

namespace MyApp.Modules.Catalog.Products.CreateProduct;

public class CreateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/products", HandleAsync)
            .WithName("CreateProduct")
            .WithTags("Products")
            .RequireAuthorization(MyApp.Auth.Jwt.Permissions.Products.Create);
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
