using MyApp.Presentation;

namespace MyApp.Features.Auth.Login;

public sealed class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", HandleAsync)
            .WithName("Login")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.AuthPolicy);
    }

    private static async Task<IResult> HandleAsync(
        LoginCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await mediator.Send(command, ct);
        return result.MatchOk(http);
    }
}
