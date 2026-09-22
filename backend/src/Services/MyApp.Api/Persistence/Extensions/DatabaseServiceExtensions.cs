// Extensions/DatabaseServiceExtensions.cs

using Microsoft.EntityFrameworkCore;

namespace MyApp.Persistence.Extensions;

public static class DatabaseServiceExtensions
{
    public static IServiceCollection AddDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
                               ?? "Data Source=products.db";

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlite(connectionString));

        return services;
    }
}
