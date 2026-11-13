using Shared.Common.Exceptions.Http;
using MyApp.Observability;

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
        app.UseMiddleware<BaggageMiddleware>();

        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapOpenApi().AllowAnonymous();

        return app;
    }
}
