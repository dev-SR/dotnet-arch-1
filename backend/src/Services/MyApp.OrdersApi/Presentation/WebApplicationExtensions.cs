using Shared.Common.Exceptions.Http;

namespace MyApp.OrdersApi.Presentation;

public static class WebApplicationExtensions
{
    public static WebApplication UseSharedPipeline(this WebApplication app)
    {
        app.UseSharedExceptionHandling();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapOpenApi().AllowAnonymous();
        app.MapHealthChecks("/health").AllowAnonymous();

        return app;
    }
}
