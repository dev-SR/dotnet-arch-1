using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyApp.Modular;
using MyApp.Modules.Catalog.Persistence;
using MyApp.Modules.Catalog.Persistence.Seeding;

namespace MyApp.Modules.Catalog;

public static class CatalogModule
{
    public static WebApplication UseCatalogModule(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<CatalogDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogModule");

        try
        {
            DbContextSchema.Apply(db);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Catalog database migrate failed");
            throw;
        }

        if (!app.Environment.IsDevelopment())
            return app;

        CatalogDataSeeder.SeedAsync(db).GetAwaiter().GetResult();
        return app;
    }
}
