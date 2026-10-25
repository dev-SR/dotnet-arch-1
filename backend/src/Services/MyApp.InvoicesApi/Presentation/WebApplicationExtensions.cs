using Shared.Common.Exceptions.Http;

namespace MyApp.InvoicesApi.Presentation;

public static class WebApplicationExtensions
{
    public static WebApplication UseSharedPipeline(this WebApplication app)
    {
        app.UseSharedExceptionHandling();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapOpenApi().AllowAnonymous();
        app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "invoices" }))
            .AllowAnonymous()
            .ExcludeFromDescription();

        return app;
    }
}
