using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MyApp.InvoicesApi;

namespace MyApp.Tests.Integration;

/// <summary>WebApplicationFactory for the invoices microservice (shared JWT secret with AuthApi tests).</summary>
public sealed class InvoicesApiFactory : WebApplicationFactory<AssemblyMarker>, IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public InvoicesApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Secret"] = "integration-test-secret-key-32chars!!",
                ["Jwt:Issuer"] = "myapp-api",
                ["Jwt:Audience"] = "myapp-clients",
                ["Jwt:AccessTokenMinutes"] = "10",
                ["Jwt:RefreshTokenDays"] = "7",
                ["ConnectionStrings:Invoices"] = "Data Source=:memory:",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<InvoicesDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.RemoveAll<IDbContextOptionsConfiguration<InvoicesDbContext>>();
            services.RemoveAll<InvoicesDbContext>();

            services.AddDbContext<InvoicesDbContext>(options =>
                options.UseSqlite(_connection));
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _connection.DisposeAsync();
        Dispose();
    }
}
