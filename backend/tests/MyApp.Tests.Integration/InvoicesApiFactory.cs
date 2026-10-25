using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.InvoicesApi;

namespace MyApp.Tests.Integration;

public sealed class InvoicesApiFactory : WebApplicationFactory<AssemblyMarker>
{
    private readonly string _dbPath;
    private readonly string _cs;
    private readonly string _rabbitMq;

    /// <summary>xUnit <c>IClassFixture</c> requires a single public constructor.</summary>
    public InvoicesApiFactory()
        : this("amqp://guest:guest@localhost:5672")
    {
    }

    private InvoicesApiFactory(string rabbitMqConnectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);
        _dbPath = Path.Combine(Path.GetTempPath(), $"myapp-invoices-{Guid.NewGuid():N}.db");
        _cs = $"Data Source={_dbPath}";
        _rabbitMq = rabbitMqConnectionString;
    }

    public static InvoicesApiFactory WithRabbitMq(string rabbitMqConnectionString) =>
        new(rabbitMqConnectionString);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Invoices", _cs);
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
                ["ConnectionStrings:Invoices"] = _cs,
                ["ConnectionStrings:RabbitMq"] = _rabbitMq,
            });
        });

        builder.ConfigureServices(services =>
        {
            foreach (var d in services.Where(x => x.ServiceType == typeof(DbContext)).ToList())
                services.Remove(d);

            services.RemoveAll<DbContextOptions<InvoicesDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<InvoicesDbContext>>();
            services.RemoveAll<InvoicesDbContext>();
            services.AddDbContext<InvoicesDbContext>(options => options.UseSqlite(_cs));
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<InvoicesDbContext>());
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
