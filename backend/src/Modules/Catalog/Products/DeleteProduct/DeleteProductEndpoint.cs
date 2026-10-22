using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.Modules.Catalog.Products.DeleteProduct;

public sealed class DeleteProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/products/{id:guid}", HandleAsync)
            .WithName("DeleteProduct")
            .WithTags("Products")
            .RequireAuthorization(MyApp.Auth.Jwt.Permissions.Products.Delete);
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
