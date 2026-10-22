using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Persistence;
using MyApp.Modules.Users.Persistence.Configurations;

namespace MyApp.Tests.Integration;

/// <summary>
/// Real multi-service story: AuthApi issues JWT; InvoicesApi validates the same HS256 secret
/// and enforces Permissions.Invoices.* + ownership.
/// </summary>
public class CrossServiceInvoiceTests : IClassFixture<MyAppFactory>, IClassFixture<InvoicesApiFactory>, IAsyncLifetime
{
    private readonly MyAppFactory _authFactory;
    private readonly InvoicesApiFactory _invoicesFactory;
    private readonly HttpClient _auth;
    private readonly HttpClient _invoices;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public CrossServiceInvoiceTests(MyAppFactory authFactory, InvoicesApiFactory invoicesFactory)
    {
        _authFactory = authFactory;
        _invoicesFactory = invoicesFactory;
        _auth = authFactory.CreateClient();
        _invoices = invoicesFactory.CreateClient();
    }

    public Task InitializeAsync()
    {
        SeedAuthAdmin();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private void SeedAuthAdmin()
    {
        using var scope = _authFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        db.Database.EnsureCreated();

        if (!db.Roles.Any())
        {
            db.Roles.AddRange(
                new Role { Id = RoleConfiguration.UserRoleId, Name = MyApp.Modules.Users.Auth.Roles.User, NormalizedName = "USER" },
                new Role { Id = RoleConfiguration.AdminRoleId, Name = MyApp.Modules.Users.Auth.Roles.Admin, NormalizedName = "ADMIN" });
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

    [Fact]
    public async Task InvoicesApi_Rejects_Request_Without_Bearer()
    {
        var response = await _invoices.PostAsJsonAsync("/invoices", new { number = "INV-1", amount = 1m });
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("AUTH.UNAUTHENTICATED");
        problem.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task CreateInvoice_Invalid_ReturnsValidationProblemDetails()
    {
        await AuthenticateInvoicesAsAdminAsync();
        var response = await _invoices.PostAsJsonAsync("/invoices", new { number = "", amount = 0m });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.GetProperty("errorCode").GetString().Should().Be("VALIDATION");
        problem.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.EnumerateObject().Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetInvoice_Missing_Returns404ProblemDetails()
    {
        await AuthenticateInvoicesAsAdminAsync();
        var response = await _invoices.GetAsync($"/invoices/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ReadProblemAsync(response)).GetProperty("errorCode").GetString()
            .Should().Be("INVOICE.NOT_FOUND");
    }

    [Fact]
    public async Task Auth_Issued_Token_Can_Create_Invoice_On_InvoicesApi()
    {
        var token = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _invoices.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var created = await _invoices.PostAsJsonAsync("/invoices", new
        {
            number = $"INV-{Guid.NewGuid():N}"[..12],
            amount = 42m,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = await created.Content.ReadFromJsonAsync<InvoiceDto>(Json);
        invoice!.Amount.Should().Be(42m);
    }

    [Fact]
    public async Task Invoice_Edit_Requires_Ownership_Unless_Admin_Across_Services()
    {
        var ownerEmail = $"owner-{Guid.NewGuid():N}@example.com";
        var otherEmail = $"other-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-password-123";
        await RegisterAsync(ownerEmail, password);
        await RegisterAsync(otherEmail, password);
        var ownerId = await FindUserIdAsync(ownerEmail);
        var otherId = await FindUserIdAsync(otherEmail);

        await AsAdminAsync();
        var roleName = $"Finance{Guid.NewGuid():N}"[..16];
        var roleResp = await _auth.PostAsJsonAsync("/auth/admin/roles", new { name = roleName });
        roleResp.EnsureSuccessStatusCode();
        var role = await roleResp.Content.ReadFromJsonAsync<RoleDto>(Json);

        foreach (var perm in new[]
                 {
                     MyApp.Auth.Jwt.Permissions.Invoices.View,
                     MyApp.Auth.Jwt.Permissions.Invoices.Edit,
                     MyApp.Auth.Jwt.Permissions.Invoices.Delete,
                 })
        {
            (await _auth.PostAsJsonAsync(
                $"/auth/admin/roles/{role!.Id}/permissions",
                new { permission = perm })).EnsureSuccessStatusCode();
        }

        (await _auth.PostAsync($"/auth/admin/users/{ownerId}/roles/{role!.Id}", null)).EnsureSuccessStatusCode();
        (await _auth.PostAsync($"/auth/admin/users/{otherId}/roles/{role.Id}", null)).EnsureSuccessStatusCode();

        var ownerToken = await LoginAsync(ownerEmail, password);
        _invoices.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);
        var created = await _invoices.PostAsJsonAsync("/invoices", new
        {
            number = $"INV-{Guid.NewGuid():N}"[..12],
            amount = 40m,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = await created.Content.ReadFromJsonAsync<InvoiceDto>(Json);

        var otherToken = await LoginAsync(otherEmail, password);
        _invoices.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);
        var forbidden = await _invoices.PutAsJsonAsync(
            $"/invoices/{invoice!.Id}",
            new { number = invoice.Number, amount = 99m });
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbidden.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ReadProblemAsync(forbidden)).GetProperty("errorCode").GetString()
            .Should().Be("INVOICE.FORBIDDEN");

        // View does not require ownership.
        (await _invoices.GetAsync($"/invoices/{invoice.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);

        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _invoices.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await _invoices.PutAsJsonAsync($"/invoices/{invoice.Id}", new { number = invoice.Number, amount = 55m }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task User_Without_Invoice_Permission_Cannot_View()
    {
        var email = $"noperm-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-password-123";
        await RegisterAsync(email, password);

        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _invoices.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var created = await _invoices.PostAsJsonAsync("/invoices", new
        {
            number = $"INV-{Guid.NewGuid():N}"[..12],
            amount = 10m,
        });
        created.EnsureSuccessStatusCode();
        var invoice = await created.Content.ReadFromJsonAsync<InvoiceDto>(Json);

        var userToken = await LoginAsync(email, password);
        _invoices.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        var forbidden = await _invoices.GetAsync($"/invoices/{invoice!.Id}");
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        forbidden.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await ReadProblemAsync(forbidden)).GetProperty("errorCode").GetString()
            .Should().Be("INVOICE.FORBIDDEN");
    }

    private async Task AuthenticateInvoicesAsAdminAsync()
    {
        var token = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _invoices.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task AsAdminAsync()
    {
        var token = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _auth.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private async Task RegisterAsync(string email, string password)
    {
        var reg = await _auth.PostAsJsonAsync("/auth/register", new { email, password });
        reg.EnsureSuccessStatusCode();
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var response = await _auth.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    private async Task<Guid> FindUserIdAsync(string email)
    {
        using var scope = _authFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<UsersDbContext>();
        return await db.Users
            .Where(u => u.NormalizedEmail == email.ToUpperInvariant())
            .Select(u => u.Id)
            .SingleAsync();
    }

    private sealed record RoleDto(Guid Id, string Name, string NormalizedName);
    private sealed record InvoiceDto(Guid Id, Guid OwnerId, string Number, decimal Amount);
}
