using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.OrdersApi.Cart.GetCart;

public sealed class GetCartEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/cart", HandleAsync)
            .WithName("GetCart")
            .WithTags("Cart")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetCartQuery(user.GetUserId()), ct);
        return result.MatchOk(http);
    }
}
