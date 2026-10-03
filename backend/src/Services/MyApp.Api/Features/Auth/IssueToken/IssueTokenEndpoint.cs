using MyApp.Features.Auth.Login;
using MyApp.Features.Auth.Refresh;
using MyApp.Presentation;

namespace MyApp.Features.Auth.IssueToken;

/// <summary>OAuth2 form adapter for Scalar password flow only. Prefer JSON /auth/login and /auth/refresh.</summary>
public sealed class IssueTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/token", HandleAsync)
            .WithName("IssueToken")
            .WithTags("Auth")
            .AllowAnonymous()
            .RequireRateLimiting(ApiRateLimiting.AuthPolicy)
            .DisableAntiforgery();
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IMediator mediator,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";

        if (!http.Request.HasFormContentType)
            return Error.Failure("AUTH.INVALID_REQUEST", "Use application/x-www-form-urlencoded.")
                .ToProblem(http);

        var form = await http.Request.ReadFormAsync(ct);

        ErrorOr<TokenResponse> result = form["grant_type"].ToString() switch
        {
            "password" => await mediator.Send(
                new LoginCommand(form["username"].ToString(), form["password"].ToString()), ct),
            "refresh_token" => await mediator.Send(
                new RefreshTokenCommand(form["refresh_token"].ToString()), ct),
            _ => Error.Failure("AUTH.UNSUPPORTED_GRANT", "unsupported_grant_type"),
        };

        return result.MatchOk(http);
    }
}
