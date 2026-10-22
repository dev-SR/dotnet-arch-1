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
/// </summary>
public class MyAppFactory : WebApplicationFactory<AssemblyMarker>
{
    private readonly SqliteConnection _users = new("DataSource=:memory:");
    private readonly SqliteConnection _catalog = new("DataSource=:memory:");
    private readonly SqliteConnection _orders = new("DataSource=:memory:");
    private readonly Dictionary<string, string?> _configOverrides;

    public MyAppFactory()
    {
        _configOverrides = new Dictionary<string, string?>();
        _users.Open();
        _catalog.Open();
        _orders.Open();
    }

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
                ["ConnectionStrings:Users"] = "Data Source=:memory:",
                ["ConnectionStrings:Catalog"] = "Data Source=:memory:",
                ["ConnectionStrings:Orders"] = "Data Source=:memory:",
            };
            foreach (var (k, v) in _configOverrides)
                defaults[k] = v;
            config.AddInMemoryCollection(defaults);
        });

        builder.ConfigureServices(services =>
        {
            ReplaceDbContext<UsersDbContext>(services, _users);
            ReplaceDbContext<CatalogDbContext>(services, _catalog);
            ReplaceDbContext<OrdersDbContext>(services, _orders);
        });
    }

    private static void ReplaceDbContext<TContext>(IServiceCollection services, SqliteConnection connection)
        where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>((sp, options) =>
        {
            options.UseSqlite(connection);
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _users.Dispose();
            _catalog.Dispose();
            _orders.Dispose();
        }
        base.Dispose(disposing);
    }
}
