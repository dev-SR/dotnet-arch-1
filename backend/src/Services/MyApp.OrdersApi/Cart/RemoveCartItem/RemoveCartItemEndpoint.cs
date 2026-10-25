using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.OrdersApi.Cart.RemoveCartItem;

public sealed class RemoveCartItemEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/cart/items/{productId:guid}", HandleAsync)
            .WithName("RemoveCartItem")
            .WithTags("Cart")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid productId,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new RemoveCartItemCommand(user.GetUserId(), productId),
            ct);
        return result.MatchNoContent(http);
    }
}
