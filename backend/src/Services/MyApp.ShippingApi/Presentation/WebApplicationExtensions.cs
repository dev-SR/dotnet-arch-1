using Shared.Common.Exceptions.Http;
using MyApp.Observability;

namespace MyApp.ShippingApi.Presentation;

public static class WebApplicationExtensions
{
    public static WebApplication UseSharedPipeline(this WebApplication app)
    {
        app.UseSharedExceptionHandling();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseMiddleware<BaggageMiddleware>();

        app.MapOpenApi().AllowAnonymous();
        app.MapHealthChecks("/health").AllowAnonymous();

        return app;
    }
}
