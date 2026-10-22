using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Modular;
using MyApp.Modules.Catalog.Persistence;
using MyApp.Modules.Catalog.Persistence.Seeding;
using Shared.Application.Persistence;

namespace MyApp.Modules.Catalog;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var cs = configuration.GetConnectionString("Catalog")
                 ?? "Data Source=catalog.db";

        services.AddTransactionalDbContext<CatalogDbContext>((sp, options) =>
        {
            options.UseSqlite(cs);
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });

        services.AddScoped<IDataSeeder, CatalogDataSeeder>();
        return services;
    }
}
