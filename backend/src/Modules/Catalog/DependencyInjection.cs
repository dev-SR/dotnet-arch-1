using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Modules.Catalog.Persistence;

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

        services.AddDbContext<CatalogDbContext>(options => options.UseSqlite(cs));
        return services;
    }
}
