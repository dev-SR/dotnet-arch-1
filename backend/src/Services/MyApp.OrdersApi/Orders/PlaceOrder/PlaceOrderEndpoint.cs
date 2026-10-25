using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.OrdersApi.Orders.PlaceOrder;

public sealed class PlaceOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/orders", HandleAsync)
            .WithName("PlaceOrder")
            .WithTags("Orders")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new PlaceOrderCommand(user.GetUserId()), ct);
        return result.MatchCreated(http, id => $"/orders/{id}");
    }
}
