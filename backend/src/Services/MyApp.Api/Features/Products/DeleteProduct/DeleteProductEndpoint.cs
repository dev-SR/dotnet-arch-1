using Shared.Common.Errors.Http;

namespace MyApp.Features.Products.DeleteProduct;

public sealed class DeleteProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/products/{id:guid}", HandleAsync)
            .WithName("DeleteProduct")
            .WithTags("Products");
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteProductCommand(id), ct);
        return result.MatchNoContent(http);
    }
}
