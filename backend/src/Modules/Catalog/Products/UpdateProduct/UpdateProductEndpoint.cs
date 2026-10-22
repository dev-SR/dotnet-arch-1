using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.Modules.Catalog.Products.UpdateProduct;

public sealed class UpdateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/products/{id:guid}", HandleAsync)
            .WithName("UpdateProduct")
            .WithTags("Products")
            .RequireAuthorization(MyApp.Auth.Jwt.Permissions.Products.Edit);
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateProductBody body,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateProductCommand(id, body.Name, body.Category, body.Price, body.Stock),
            ct);
        return result.MatchNoContent(http);
    }

    public sealed record UpdateProductBody(
        string Name,
        string Category,
        decimal Price,
        int Stock);
}
