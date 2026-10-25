namespace MyApp.InvoicesApi.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        db.Database.EnsureCreated();
        return app;
    }
}
