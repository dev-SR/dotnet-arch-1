using MyApp.Modular;

namespace MyApp.InvoicesApi.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        DbContextSchema.Apply(db);
        return app;
    }
}
