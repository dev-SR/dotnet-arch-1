using Microsoft.AspNetCore.Http.HttpResults;

namespace MyApp.Features.Products.CreateProduct;
using Carter;
public class CreateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/products", HandleAsync)
            .WithName("CreateProduct")
            .WithTags("Products");
    }
    private async Task<Created<Guid>> HandleAsync(
        CreateProductCommand command,
        IMediator mediator,
        CancellationToken ct)
    {
        var id = await mediator.Send(command, ct);
        return TypedResults.Created($"/products/{id}", id);
    }
}
