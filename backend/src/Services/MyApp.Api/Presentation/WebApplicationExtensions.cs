using Scalar.AspNetCore;
using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;

public static class WebApplicationExtensions
{
    extension(WebApplication app)
    {
        public void UsePresentation()
        {
            // Before endpoints — shared handler from Shared.Common
            app.UseSharedExceptionHandling();

            app.MapApiRoutes();
            app.MapApiDocumentation();
        }

        private void MapApiRoutes()
        {
            // 1. Map all Carter REPR Endpoints
            app.MapCarter();

            // 2. Map Health Checks
            app.MapHealthChecks("/health");
        }

        private void MapApiDocumentation()
        {
            if (!app.Environment.IsDevelopment()) return;
            app.MapOpenApi();
            app.MapScalarApiReference(); // Scalar is the modern alternative to Swagger UI
        }
    }
}
