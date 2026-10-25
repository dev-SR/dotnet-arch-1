using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.ShippingApi;
using MyApp.ShippingApi.Persistence;

namespace MyApp.Tests.Integration;

public sealed class ShippingApiFactory : WebApplicationFactory<AssemblyMarker>
{
    private readonly string _dbPath;
    private readonly string _cs;
    private readonly string _rabbitMq;

    private ShippingApiFactory(string rabbitMqConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);
        _dbPath = Path.Combine(Path.GetTempPath(), $"myapp-shipping-{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        _rabbitMq = rabbitMqConnectionString;
    }

    public static ShippingApiFactory WithRabbitMq(string rabbitMqConnectionString) =>
        new(rabbitMqConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Shipping", _cs);
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "integration-test-secret-key-32chars!!",
                ["Jwt:Issuer"] = "myapp-api",
                ["Jwt:Audience"] = "myapp-clients",
                ["ConnectionStrings:Shipping"] = _cs,
                ["ConnectionStrings:RabbitMq"] = _rabbitMq,
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(x => x.ServiceType == typeof(DbContext)).ToList())
                services.Remove(d);

            services.RemoveAll<DbContextOptions<ShippingDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ShippingDbContext>>();
            services.RemoveAll<ShippingDbContext>();
            services.AddDbContext<ShippingDbContext>(o => o.UseSqlite(_cs));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<ShippingDbContext>());
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
