using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.ShippingApi.GetShipmentByOrder;

public sealed class GetShipmentByOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/shipping/by-order/{orderId:guid}", HandleAsync)
            .WithName("GetShipmentByOrder")
            .WithTags("Shipping")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid orderId,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetShipmentByOrderQuery(orderId, user.GetUserId()),
            ct);
        return result.MatchOk(http);
    }
}
