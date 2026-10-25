using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.OrdersApi;
using MyApp.OrdersApi.Persistence;

namespace MyApp.Tests.Integration;

public sealed class OrdersApiFactory : WebApplicationFactory<AssemblyMarker>
{
    private readonly string _dbPath;
    private readonly string _cs;
    private readonly string _rabbitMq;

    private OrdersApiFactory(string rabbitMqConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);
        _dbPath = Path.Combine(Path.GetTempPath(), $"myapp-orders-{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        _rabbitMq = rabbitMqConnectionString;
    }

    public static OrdersApiFactory WithRabbitMq(string rabbitMqConnectionString) =>
        new(rabbitMqConnectionString);

    public string ConnectionString => _cs;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Orders", _cs);
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "integration-test-secret-key-32chars!!",
                ["Jwt:Issuer"] = "myapp-api",
                ["Jwt:Audience"] = "myapp-clients",
                ["Jwt:AccessTokenMinutes"] = "10",
                ["Jwt:RefreshTokenDays"] = "7",
                ["ConnectionStrings:Orders"] = _cs,
                ["ConnectionStrings:RabbitMq"] = _rabbitMq,
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(x => x.ServiceType == typeof(DbContext)).ToList())
                services.Remove(d);

            services.RemoveAll<DbContextOptions<OrdersDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<OrdersDbContext>>();
            services.RemoveAll<OrdersDbContext>();
            services.AddDbContext<OrdersDbContext>((sp, o) =>
            {
                o.UseSqlite(_cs);
                o.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
            });
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<OrdersDbContext>());
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                if (File.Exists(_dbPath))
                    File.Delete(_dbPath);
            }
            catch
            {
                // best-effort
            }
        }
        base.Dispose(disposing);
    }
}
