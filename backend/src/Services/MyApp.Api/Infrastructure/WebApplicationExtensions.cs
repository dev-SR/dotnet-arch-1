using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;
using MyApp.Persistence.Seeding;

namespace MyApp.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Database.Migrate();

        if (app.Environment.IsDevelopment())
            DataSeeder.SeedAsync(db).GetAwaiter().GetResult();

        return app;
    }
}
