using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.OrdersApi.Orders.GetOrder;

public sealed class GetOrderEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/orders/{id:guid}", HandleAsync)
            .WithName("GetOrder")
            .WithTags("Orders")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new GetOrderQuery(id, user.GetUserId(), user.IsInRole("Admin")),
            ct);
        return result.MatchOk(http);
    }
}
