namespace MyApp.Modules.Users.Auth.AuthDemo;

/// <summary>Demo endpoints for the WorkingHours and EnhancedSecurity policies.</summary>
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
    }
}
