using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.ApiHost;
using MyApp.Modules.Catalog.Persistence;
using MyApp.Modules.Orders.Persistence;
using MyApp.Modules.Users.Persistence;

namespace MyApp.Tests.Integration;

/// <summary>
/// Host factory for ApiHost. Uses <see cref="AssemblyMarker"/> because InvoicesApi also has a Program.
/// File-based temp SQLite — Wolverine durability rejects in-memory databases.
/// </summary>
public class MyAppFactory : WebApplicationFactory<AssemblyMarker>
{
    private readonly string _usersPath;
    private readonly string _catalogPath;
    private readonly string _ordersPath;
    private readonly string _usersCs;
    private readonly string _catalogCs;
    private readonly string _ordersCs;
    private readonly Dictionary<string, string?> _configOverrides;

    public MyAppFactory()
    {
        var id = Guid.NewGuid().ToString("N");
        _usersPath = Path.Combine(Path.GetTempPath(), $"myapp-users-{id}.db");
        _catalogPath = Path.Combine(Path.GetTempPath(), $"myapp-catalog-{id}.db");
        _ordersPath = Path.Combine(Path.GetTempPath(), $"myapp-orders-{id}.db");
        _usersCs = $"Data Source={_usersPath}";
        _catalogCs = $"Data Source={_catalogPath}";
        _ordersCs = $"Data Source={_ordersPath}";

        Environment.SetEnvironmentVariable("ConnectionStrings__Users", _usersCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Catalog", _catalogCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Orders", _ordersCs);

        _configOverrides = new Dictionary<string, string?>();
    }

    public string CatalogConnectionString => _catalogCs;
    public string OrdersConnectionString => _ordersCs;

    public static MyAppFactory WithConfig(Dictionary<string, string?> overrides)
    {
        var factory = new MyAppFactory();
        foreach (var (k, v) in overrides)
            factory._configOverrides[k] = v;
        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Users", _usersCs);
        builder.UseSetting("ConnectionStrings:Catalog", _catalogCs);
        builder.UseSetting("ConnectionStrings:Orders", _ordersCs);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var defaults = new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "integration-test-secret-key-32chars!!",
                ["Jwt:Issuer"] = "myapp-api",
                ["Jwt:Audience"] = "myapp-clients",
                ["Jwt:AccessTokenMinutes"] = "10",
                ["Jwt:RefreshTokenDays"] = "7",
                ["RateLimit:GlobalPerMinute"] = "10000",
                ["RateLimit:AuthPerMinute"] = "100",
                ["Seed:AdminEmail"] = "",
                ["Seed:AdminPassword"] = "",
                ["ConnectionStrings:Users"] = _usersCs,
                ["ConnectionStrings:Catalog"] = _catalogCs,
                ["ConnectionStrings:Orders"] = _ordersCs,
            };
            foreach (var (k, v) in _configOverrides)
                defaults[k] = v;
            config.AddInMemoryCollection(defaults);
        });

        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(x => x.ServiceType == typeof(DbContext)).ToList())
                services.Remove(d);

            ReplaceDbContext<UsersDbContext>(services, _usersCs);
            ReplaceDbContext<CatalogDbContext>(services, _catalogCs);
            ReplaceDbContext<OrdersDbContext>(services, _ordersCs);

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<UsersDbContext>());
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<CatalogDbContext>());
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<OrdersDbContext>());
        });
    }

    private static void ReplaceDbContext<TContext>(IServiceCollection services, string connectionString)
        where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            TryDelete(_usersPath);
            TryDelete(_catalogPath);
            TryDelete(_ordersPath);
        }
        base.Dispose(disposing);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
