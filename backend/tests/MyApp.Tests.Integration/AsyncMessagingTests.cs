using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.InvoicesApi;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Persistence;
using MyApp.Modules.Users.Persistence.Configurations;
using MyApp.OrdersApi.Persistence;
using MyApp.OrdersApi.Consumers.ProjectProductCatalog;
using MyApp.ShippingApi.Persistence;

namespace MyApp.Tests.Integration;

public class AsyncMessagingTests :
    IClassFixture<RabbitMqFixture>,
    IClassFixture<ElasticsearchFixture>,
    IAsyncLifetime
{
    private readonly RabbitMqFixture _rabbit;
    private readonly ElasticsearchFixture _elasticsearch;
    private MyAppFactory _apiHost = null!;
    private OrdersApiFactory _orders = null!;
    private InvoicesApiFactory _invoices = null!;
    private ShippingApiFactory _shipping = null!;
    private HttpClient _apiClient = null!;
    private HttpClient _ordersClient = null!;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public AsyncMessagingTests(RabbitMqFixture rabbit, ElasticsearchFixture elasticsearch)
    {
        _rabbit = rabbit;
        _elasticsearch = elasticsearch;
    }

    public Task InitializeAsync()
    {
        _apiHost = MyAppFactory.WithInfra(_rabbit.ConnectionString, _elasticsearch.Uri);
        _orders = OrdersApiFactory.WithRabbitMq(_rabbit.ConnectionString);
        _invoices = InvoicesApiFactory.WithRabbitMq(_rabbit.ConnectionString);
        _shipping = ShippingApiFactory.WithRabbitMq(_rabbit.ConnectionString);

        _apiClient = _apiHost.CreateClient();
        _ordersClient = _orders.CreateClient();
        _ = _invoices.CreateClient();
        _ = _shipping.CreateClient();
        SeedAdmin();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _apiClient.Dispose();
        _ordersClient.Dispose();
        _apiHost.Dispose();
        _orders.Dispose();
        _invoices.Dispose();
        _shipping.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task CreateProduct_Is_Searchable_Via_Search_Module()
    {
        await AuthenticateAsync(_apiClient);

        var name = $"Search-{Guid.NewGuid():N}"[..14];
        var create = await _apiClient.PostAsJsonAsync("/products", new
        {
            Name = name,
            Category = "Async",
            Price = 42.5m,
            Stock = 2,
        });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = await create.Content.ReadFromJsonAsync<Guid>(Json);

        await WaitForAsync(async () =>
        {
            var response = await _apiClient.GetAsync($"/search/products?q={Uri.EscapeDataString(name)}");
            if (!response.IsSuccessStatusCode)
                return false;
            var hits = await response.Content.ReadFromJsonAsync<List<SearchHitDto>>(Json);
            return hits is not null && hits.Any(h => h.ProductId == id);
        });

        var search = await _apiClient.GetAsync($"/search/products?q={Uri.EscapeDataString(name)}");
        search.EnsureSuccessStatusCode();
        var results = await search.Content.ReadFromJsonAsync<List<SearchHitDto>>(Json);
        results.Should().Contain(h => h.ProductId == id && h.Name == name && h.Price == 42.5m);
    }

    [Fact]
    public async Task CreateProduct_Projects_Into_Orders_ProductCatalog()
    {
        await AuthenticateAsync(_apiClient);
        await AuthenticateAsync(_ordersClient);

        var name = $"Msg-{Guid.NewGuid():N}"[..12];
        var create = await _apiClient.PostAsJsonAsync("/products", new
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
            using var scope = _orders.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            return await db.ProductCatalog.AnyAsync(x => x.ProductId == id);
        });

        using var scope = _orders.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var row = await orders.ProductCatalog.SingleAsync(x => x.ProductId == id);
        row.Name.Should().Be(name);
        row.Price.Should().Be(19.99m);
    }

    [Fact]
    public async Task Failed_CreateProduct_Does_Not_Project_To_Orders()
    {
        await AuthenticateAsync(_apiClient);
        var before = await CountProductCatalogAsync();

        var response = await _apiClient.PostAsJsonAsync("/products", new
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
    public async Task PlaceOrder_Creates_Invoice_And_Shipment()
    {
        await AuthenticateAsync(_apiClient);
        await AuthenticateAsync(_ordersClient);

        var name = $"Ord-{Guid.NewGuid():N}"[..12];
        var create = await _apiClient.PostAsJsonAsync("/products", new
        {
            Name = name,
            Category = "Async",
            Price = 12.5m,
            Stock = 5,
        });
        create.EnsureSuccessStatusCode();
        var productId = await create.Content.ReadFromJsonAsync<Guid>(Json);

        await WaitForAsync(async () =>
        {
            using var scope = _orders.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            return await db.ProductCatalog.AnyAsync(x => x.ProductId == productId);
        });

        var add = await _ordersClient.PutAsJsonAsync("/cart/items", new { ProductId = productId, Quantity = 2 });
        add.EnsureSuccessStatusCode();

        var place = await _ordersClient.PostAsync("/orders/", null);
        place.StatusCode.Should().Be(HttpStatusCode.Created);
        var orderId = await place.Content.ReadFromJsonAsync<Guid>(Json);

        var invoiceNumber = $"ORD-{orderId:N}"[..20];
        await WaitForAsync(async () =>
        {
            using var scope = _invoices.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
            return await db.Invoices.AnyAsync(i => i.Number == invoiceNumber);
        });

        await WaitForAsync(async () =>
        {
            using var scope = _shipping.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            return await db.Shipments.AnyAsync(s => s.OrderId == orderId);
        });

        using (var scope = _shipping.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShippingDbContext>();
            var shipment = await db.Shipments.SingleAsync(s => s.OrderId == orderId);
            shipment.Status.Should().Be(MyApp.ShippingApi.ShipmentStatus.Pending);
        }
    }

    [Fact]
    public async Task Duplicate_Product_Projection_Is_Idempotent()
    {
        await AuthenticateAsync(_apiClient);
        var name = $"Idem-{Guid.NewGuid():N}"[..12];
        var create = await _apiClient.PostAsJsonAsync("/products", new
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
            using var scope = _orders.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
            return await db.ProductCatalog.AnyAsync(x => x.ProductId == id);
        });

        using var scope = _orders.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        await new ProductCreatedIntegrationEventHandler().Handle(
            new MyApp.Messaging.IntegrationEvents.ProductCreatedIntegrationEvent(id, name, 8m),
            orders,
            CancellationToken.None);
        (await orders.ProductCatalog.CountAsync(x => x.ProductId == id)).Should().Be(1);
    }

    private async Task AuthenticateAsync(HttpClient client)
    {
        var login = await _apiClient.PostAsJsonAsync("/auth/login", new
        {
            email = "admin@example.com",
            password = "ChangeMe-12345",
        });
        login.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await login.Content.ReadAsStreamAsync());
        var token = doc.RootElement.GetProperty("access_token").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private void SeedAdmin()
    {
        using var scope = _apiHost.Services.CreateScope();
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
        using var scope = _orders.Services.CreateScope();
        var orders = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        return await orders.ProductCatalog.CountAsync();
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition, int timeoutMs = 30000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Exception? last = null;
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            try
            {
                if (await condition())
                    return;
                last = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                // Transient while hosts/ES settle, or abort during teardown — keep polling.
                last = ex;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("Condition was not met within timeout.", last);
    }

    private sealed record SearchHitDto(Guid ProductId, string Name, decimal Price);
}
