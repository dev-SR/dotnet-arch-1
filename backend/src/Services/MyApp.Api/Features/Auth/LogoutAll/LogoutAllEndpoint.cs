using System.Security.Claims;

namespace MyApp.Features.Auth.LogoutAll;

public sealed class LogoutAllEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout-all", HandleAsync)
            .WithName("LogoutAll")
            .WithTags("Auth")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        ClaimsPrincipal principal,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new LogoutAllCommand(principal.GetUserId()), ct);
        return result.MatchNoContent(http);
    }
}
