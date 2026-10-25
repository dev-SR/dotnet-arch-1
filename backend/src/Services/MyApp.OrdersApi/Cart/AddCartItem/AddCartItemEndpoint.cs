using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.OrdersApi.Cart.AddCartItem;

public sealed record AddCartItemRequest(Guid ProductId, int Quantity);

public sealed class AddCartItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/cart/items", HandleAsync)
            .WithName("AddCartItem")
            .WithTags("Cart")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        AddCartItemRequest body,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new AddCartItemCommand(user.GetUserId(), body.ProductId, body.Quantity),
            ct);
        return result.MatchNoContent(http);
    }
}
