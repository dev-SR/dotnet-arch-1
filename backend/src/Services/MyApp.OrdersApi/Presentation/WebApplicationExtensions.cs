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
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "orders" }))
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }
}
