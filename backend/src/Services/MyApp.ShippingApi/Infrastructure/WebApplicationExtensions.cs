using MyApp.ShippingApi.Persistence;

namespace MyApp.ShippingApi.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
        db.Database.EnsureCreated();
        return app;
    }
}
