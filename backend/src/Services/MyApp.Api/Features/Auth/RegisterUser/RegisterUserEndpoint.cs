using MyApp.Presentation;

namespace MyApp.Features.Auth.RegisterUser;

public sealed class RegisterUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", HandleAsync)
            .WithName("RegisterUser")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.AuthPolicy);
    }

    private static async Task<IResult> HandleAsync(
        RegisterUserCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.MatchNoContent(http);
    }
}
