using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.Persistence;

namespace MyApp.Tests.Integration;

public class MyAppFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Dictionary<string, string?> _configOverrides;

    public MyAppFactory()
    {
        _configOverrides = new Dictionary<string, string?>();
        _connection.Open();
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
            };
            foreach (var (k, v) in _configOverrides)
                defaults[k] = v;
            config.AddInMemoryCollection(defaults);
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.RemoveAll<AppDbContext>();

            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _connection.Dispose();
        base.Dispose(disposing);
    }
}
