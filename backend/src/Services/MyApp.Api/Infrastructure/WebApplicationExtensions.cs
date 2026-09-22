using MyApp.Persistence;

namespace MyApp.Infrastructure;

// Extensions/InfrastructureApplicationBuilderExtensions.cs
public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Runs synchronously during the app build phase
        db.Database.EnsureCreated();

        return app;
    }
}
