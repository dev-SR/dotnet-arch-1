

namespace MyApp.Modules.Users.Auth.Logout;

public sealed class LogoutEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/logout", HandleAsync)
            .WithName("Logout")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(UsersRateLimiting.AuthPolicy);
    }

    private static async Task<IResult> HandleAsync(
        LogoutCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.MatchNoContent(http);
    }
}
