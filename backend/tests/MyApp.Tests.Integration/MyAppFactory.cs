using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.ApiHost;
using MyApp.Modules.Catalog.Persistence;
using MyApp.Modules.Users.Persistence;

namespace MyApp.Tests.Integration;

public class MyAppFactory : WebApplicationFactory<AssemblyMarker>
{
    private readonly string _usersPath;
    private readonly string _catalogPath;
    private readonly string _usersCs;
    private readonly string _catalogCs;
    private readonly string _rabbitMq;
    private readonly string _elasticsearchUri;
    private readonly Dictionary<string, string?> _configOverrides;

    /// <summary>xUnit <c>IClassFixture</c> requires a single public constructor.</summary>
    public MyAppFactory()
        : this("amqp://guest:guest@localhost:5672", "http://localhost:9200", null)
    {
    }

    private MyAppFactory(
        string rabbitMqConnectionString,
        string elasticsearchUri,
        Dictionary<string, string?>? configOverrides)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rabbitMqConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(elasticsearchUri);
        var id = Guid.NewGuid().ToString("N");
        _usersPath = Path.Combine(Path.GetTempPath(), $"myapp-users-{id}.db");
        _catalogPath = Path.Combine(Path.GetTempPath(), $"myapp-catalog-{id}.db");
        _usersCs = $"Data Source={_usersPath}";
        _catalogCs = $"Data Source={_catalogPath}";
        _rabbitMq = rabbitMqConnectionString;
        _elasticsearchUri = elasticsearchUri;
        _configOverrides = configOverrides ?? new Dictionary<string, string?>();

        Environment.SetEnvironmentVariable("ConnectionStrings__Users", _usersCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__Catalog", _catalogCs);
        Environment.SetEnvironmentVariable("ConnectionStrings__RabbitMq", _rabbitMq);
        Environment.SetEnvironmentVariable("Elasticsearch__Uri", _elasticsearchUri);
    }

    public string CatalogConnectionString => _catalogCs;
    public string RabbitMqConnectionString => _rabbitMq;
    public string ElasticsearchUri => _elasticsearchUri;

    public static MyAppFactory WithInfra(string rabbitMqConnectionString, string elasticsearchUri) =>
        new(rabbitMqConnectionString, elasticsearchUri, null);

    public static MyAppFactory WithRabbitMq(string rabbitMqConnectionString) =>
        WithInfra(rabbitMqConnectionString, "http://localhost:9200");

    public static MyAppFactory WithConfig(
        Dictionary<string, string?> overrides,
        string? rabbitMq = null,
        string? elasticsearchUri = null) =>
        new(
            rabbitMq ?? "amqp://guest:guest@localhost:5672",
            elasticsearchUri ?? "http://localhost:9200",
            new Dictionary<string, string?>(overrides));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Users", _usersCs);
        builder.UseSetting("ConnectionStrings:Catalog", _catalogCs);
        builder.UseSetting("ConnectionStrings:RabbitMq", _rabbitMq);
        builder.UseSetting("Elasticsearch:Uri", _elasticsearchUri);

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
                ["ConnectionStrings:RabbitMq"] = _rabbitMq,
                ["Elasticsearch:Uri"] = _elasticsearchUri,
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

            services.AddScoped<DbContext>(sp => sp.GetRequiredService<UsersDbContext>());
            services.AddScoped<DbContext>(sp => sp.GetRequiredService<CatalogDbContext>());
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
            // best-effort
        }
    }
}
