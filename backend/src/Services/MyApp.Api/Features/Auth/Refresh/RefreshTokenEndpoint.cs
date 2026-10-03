using MyApp.Presentation;

namespace MyApp.Features.Auth.Refresh;

public sealed class RefreshTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/refresh", HandleAsync)
            .WithName("RefreshToken")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.AuthPolicy);
    }

    private static async Task<IResult> HandleAsync(
        RefreshTokenCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await mediator.Send(command, ct);
        return result.MatchOk(http);
    }
}
