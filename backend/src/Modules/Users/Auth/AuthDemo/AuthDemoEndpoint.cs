using System.Security.Claims;

namespace MyApp.Modules.Users.Auth.AuthDemo;

/// <summary>Demo endpoints for WorkingHours / EnhancedSecurity policies (Learn 02).</summary>
public sealed class AuthDemoEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/demo")
            .WithTags("AuthDemo")
            .RequireAuthorization();

        group.MapGet("/working-hours", () => Results.Ok(new { ok = true, policy = Policies.WorkingHours }))
            .RequireAuthorization(Policies.WorkingHours)
            .WithName("DemoWorkingHours");

        group.MapGet("/enhanced-security", () => Results.Ok(new { ok = true, policy = Policies.EnhancedSecurity }))
            .RequireAuthorization(Policies.EnhancedSecurity)
            .WithName("DemoEnhancedSecurity");

        group.MapPost("/mfa-token", IssueMfaTokenAsync)
            .WithName("DemoMfaToken");
    }

    private static async Task<IResult> IssueMfaTokenAsync(
        ClaimsPrincipal principal,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new IssueMfaTokenCommand(principal), ct);
        return result.MatchOk(http);
    }
}
