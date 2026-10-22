using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Messaging.IntegrationEvents;
using MyApp.Modules.Orders.Catalog;
using MyApp.Modules.Orders.Persistence;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Persistence;
using MyApp.Modules.Users.Persistence.Configurations;

namespace MyApp.Tests.Integration;

public class AsyncMessagingTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public AsyncMessagingTests(MyAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedAdmin();
    }

    [Fact]
    public async Task CreateProduct_Projects_Into_Orders_ProductCatalog()
    {
        await AuthenticateAdminAsync();
        var name = $"Msg-{Guid.NewGuid():N}"[..12];
        var create = await _client.PostAsJsonAsync("/products", new
        {
            Name = name,
            Category = "Async",
            Price = 19.99m,
            Stock = 3,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>(Json);

        await WaitForAsync(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            return await orders.ProductCatalog.AnyAsync(x => x.ProductId == id);
        });

        using (var scope = _factory.Services.CreateScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var row = await orders.ProductCatalog.SingleAsync(x => x.ProductId == id);
            row.Name.Should().Be(name);
            row.Price.Should().Be(19.99m);
        }
    }

    [Fact]
    public async Task CreateProduct_Builds_Wolverine_Message_Tables_In_Catalog_Db()
    {
        await AuthenticateAdminAsync();
        (await CatalogHasAnyWolverineTableAsync()).Should().BeTrue(
            "AutoBuildMessageStorageOnStartup should create Wolverine envelope tables");

        var create = await _client.PostAsJsonAsync("/products", new
        {
            Name = $"Out-{Guid.NewGuid():N}"[..12],
            Category = "Async",
            Price = 5m,
            Stock = 1,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>(Json);

        await WaitForAsync(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            return await orders.ProductCatalog.AnyAsync(x => x.ProductId == id);
        });
    }

    [Fact]
    public async Task Failed_CreateProduct_Does_Not_Project_To_Orders()
    {
        await AuthenticateAdminAsync();
        var before = await CountProductCatalogAsync();

        var response = await _client.PostAsJsonAsync("/products", new
        {
            Name = "ab",
            Category = "x",
            Price = 0,
            Stock = -1,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await Task.Delay(500);
        (await CountProductCatalogAsync()).Should().Be(before);
    }

    [Fact]
    public async Task Duplicate_Delivery_Is_Idempotent_On_ProductCatalog()
    {
        await AuthenticateAdminAsync();
        var name = $"Idem-{Guid.NewGuid():N}"[..12];
        var create = await _client.PostAsJsonAsync("/products", new
        {
            Name = name,
            Category = "Async",
            Price = 8m,
            Stock = 1,
        });
        create.EnsureSuccessStatusCode();
        var id = await create.Content.ReadFromJsonAsync<Guid>(Json);

        await WaitForAsync(async () =>
        {
            using var scope = _factory.Services.CreateScope();
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            return await orders.ProductCatalog.AnyAsync(x => x.ProductId == id);
        });

        // Handler is idempotent: re-insert same key should not throw / not duplicate.
        using (var scope = _factory.Services.CreateScope())
        {
            var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            var handlerExists = await orders.ProductCatalog.CountAsync(x => x.ProductId == id);
            handlerExists.Should().Be(1);

            // Simulate second handle
            await new ProductCreatedIntegrationEventHandler().Handle(
                new ProductCreatedIntegrationEvent(id, name, 8m),
                orders,
                CancellationToken.None);
            await orders.SaveChangesAsync();
            (await orders.ProductCatalog.CountAsync(x => x.ProductId == id)).Should().Be(1);
        }
    }

    private async Task AuthenticateAdminAsync()
    {
        var login = await _client.PostAsJsonAsync("/auth/login", new
        {
            email = "admin@example.com",
            password = "ChangeMe-12345",
        });
        login.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var token = doc.RootElement.GetProperty("access_token").GetString()!;
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void SeedAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        db.Database.EnsureCreated();

        if (!db.Roles.Any())
        {
            db.Roles.AddRange(
                new Role { Id = RoleConfiguration.UserRoleId, Name = Roles.User, NormalizedName = "USER" },
                new Role { Id = RoleConfiguration.AdminRoleId, Name = Roles.Admin, NormalizedName = "ADMIN" });
            db.SaveChanges();
        }

        if (!db.Permissions.Any())
        {
            db.Permissions.AddRange(PermissionConfiguration.SeedRows
                .Select(r => new Permission { Id = r.Id, Name = r.Name }));
            db.SaveChanges();
        }

        if (!db.RolePermissions.Any())
        {
            db.RolePermissions.Add(new RolePermission
            {
                RoleId = RoleConfiguration.UserRoleId,
                PermissionId = PermissionConfiguration.ProductsViewId,
            });
            db.RolePermissions.AddRange(PermissionConfiguration.SeedRows.Select(p => new RolePermission
            {
                RoleId = RoleConfiguration.AdminRoleId,
                PermissionId = p.Id,
            }));
            db.SaveChanges();
        }

        if (db.Users.Any(u => u.NormalizedEmail == "ADMIN@EXAMPLE.COM"))
            return;

        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
        var admin = new User
        {
            Email = "admin@example.com",
            NormalizedEmail = "ADMIN@EXAMPLE.COM",
            PasswordHash = "",
        };
        admin.PasswordHash = hasher.HashPassword(admin, "ChangeMe-12345");
        db.Users.Add(admin);
        db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = RoleConfiguration.AdminRoleId });
        db.SaveChanges();
    }

    private async Task<int> CountProductCatalogAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await orders.ProductCatalog.CountAsync();
    }

    private async Task<bool> CatalogHasAnyWolverineTableAsync()
    {
        await using var conn = new SqliteConnection(_factory.CatalogConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name LIKE 'wolverine%'";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 10000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (await condition())
                return;
            await Task.Delay(100);
        }

        throw new TimeoutException("Condition was not met within timeout.");
    }
}
