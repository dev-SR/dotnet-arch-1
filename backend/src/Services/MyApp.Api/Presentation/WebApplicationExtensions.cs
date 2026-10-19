using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;

public static class WebApplicationExtensions
{
    public static WebApplication UsePresentation(this WebApplication app)
    {
        app.UseSharedExceptionHandling();
        app.UseForwardedHeaders();
        app.UseCors(app.Environment.IsDevelopment() ? "Development" : "Production");
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapCarter();
        app.MapHealthChecks("/health").AllowAnonymous();

        // OpenAPI JSON only — Scalar UI lives on MyApp.Gateway (/scalar).
        app.MapOpenApi().AllowAnonymous();

        return app;
    }
}
