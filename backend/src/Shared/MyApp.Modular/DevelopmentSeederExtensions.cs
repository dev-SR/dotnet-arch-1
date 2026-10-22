using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MyApp.Modular;

public static class DevelopmentSeederExtensions
{
    /// <summary>
    /// Runs every registered <see cref="IDataSeeder"/> in Development (after module schema Use*).
    /// </summary>
    public static WebApplication UseDevelopmentSeeders(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
            return app;

        using var scope = app.Services.CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
            seeder.SeedAsync().GetAwaiter().GetResult();

        return app;
    }
}
