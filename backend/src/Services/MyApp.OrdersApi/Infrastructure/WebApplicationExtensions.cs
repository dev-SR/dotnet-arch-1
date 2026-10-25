using MyApp.OrdersApi.Persistence;

namespace MyApp.OrdersApi.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        db.Database.EnsureCreated();
        return app;
    }
}
