using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;

public static class WebApplicationExtensions
{
    public static WebApplication UseSharedPipeline(this WebApplication app)
    {
        app.UseSharedExceptionHandling();
        app.UseForwardedHeaders();
        app.UseCors(app.Environment.IsDevelopment() ? "Development" : "Production");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapOpenApi().AllowAnonymous();

        return app;
    }

    /// <summary>Backward-compatible alias.</summary>
    public static WebApplication UsePresentation(this WebApplication app) => app.UseSharedPipeline();
}
