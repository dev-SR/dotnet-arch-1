using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Features.Auth;
using MyApp.Persistence;
using MyApp.Persistence.Configurations;

namespace MyApp.Tests.Integration;

public class AuthSecurityTests : IClassFixture<MyAppFactory>
{
    private readonly MyAppFactory _factory;
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public AuthSecurityTests(MyAppFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        SeedRolesAndAdmin();
    }

    private void SeedRolesAndAdmin()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        if (!db.Roles.Any())
        {
            db.Roles.AddRange(
                new Role { Id = RoleConfiguration.UserRoleId, Name = Roles.User, NormalizedName = "USER" },
                new Role { Id = RoleConfiguration.AdminRoleId, Name = Roles.Admin, NormalizedName = "ADMIN" });
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
        db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = RoleConfiguration.UserRoleId });
        db.SaveChanges();
    }

    private void ClearAuth() => _client.DefaultRequestHeaders.Authorization = null;

    // ── Anonymous / problem details ───────────────────────────────────────

    [Fact]
    public async Task Health_Is_Anonymous()
    {
        ClearAuth();
        (await _client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Protected_Without_Bearer_Returns_401_With_WwwAuthenticate()
    {
        ClearAuth();
        var response = await _client.GetAsync("/auth/admin/users");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Headers.WwwAuthenticate.Should().NotBeEmpty();
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    // ── Roles (RoleClaimType) ─────────────────────────────────────────────

    [Fact]
    public async Task AdminOnly_NonAdmin_Gets_403_Admin_Gets_200()
    {
        ClearAuth();
        var userToken = await RegisterAndLoginAsync($"user-{Guid.NewGuid():N}@example.com", "A-long-password-123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Login / refresh / token adapter ───────────────────────────────────

    [Fact]
    public async Task Login_Returns_Access_And_Refresh_Tokens()
    {
        ClearAuth();
        var tokens = await LoginFullAsync("admin@example.com", "ChangeMe-12345");
        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.TokenType.Should().Be("Bearer");
        tokens.ExpiresIn.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Refresh_HappyPath_Issues_New_Pair()
    {
        ClearAuth();
        var login = await LoginFullAsync("admin@example.com", "ChangeMe-12345");
        var rotated = await RefreshAsync(login.RefreshToken);
        rotated.AccessToken.Should().NotBe(login.AccessToken);
        rotated.RefreshToken.Should().NotBe(login.RefreshToken);
    }

    [Fact]
    public async Task Refresh_Reuse_Kills_Family()
    {
        ClearAuth();
        var login = await LoginFullAsync("admin@example.com", "ChangeMe-12345");
        var refresh1 = login.RefreshToken;

        var rotated = await RefreshAsync(refresh1);
        var refresh2 = rotated.RefreshToken;

        var replay = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh1 });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var newestDead = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = refresh2 });
        newestDead.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Auth_Token_Password_Grant_Adapter_Works()
    {
        ClearAuth();
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = "admin@example.com",
            ["password"] = "ChangeMe-12345",
        });
        var response = await _client.PostAsync("/auth/token", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await response.Content.ReadFromJsonAsync<TokenDto>(Json);
        tokens!.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Auth_Token_Refresh_Grant_Adapter_Works()
    {
        ClearAuth();
        var login = await LoginFullAsync("admin@example.com", "ChangeMe-12345");
        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = login.RefreshToken,
        });
        var response = await _client.PostAsync("/auth/token", content);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<TokenDto>(Json))!.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    // ── Lockout + IPersistOnFailure ───────────────────────────────────────

    [Fact]
    public async Task Five_Wrong_Passwords_Locks_Account_And_Blocks_Correct_Password()
    {
        ClearAuth();
        var email = $"lockme-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-password-123";
        await RegisterAndLoginAsync(email, password);

        for (var i = 0; i < 5; i++)
        {
            var bad = await _client.PostAsJsonAsync("/auth/login", new { email, password = "wrong-password!!" });
            bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
            user.LockoutEnd.Should().NotBeNull();
            user.LockoutEnd!.Value.Should().BeAfter(DateTimeOffset.UtcNow);
        }

        var stillLocked = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        stillLocked.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Ban / unban / self-ban ────────────────────────────────────────────

    [Fact]
    public async Task Ban_Makes_Access_Token_401()
    {
        ClearAuth();
        var victimEmail = $"victim-{Guid.NewGuid():N}@example.com";
        var victimAccess = await RegisterAndLoginAsync(victimEmail, "A-long-password-123");
        var victimId = await FindUserIdAsync(victimEmail);

        await BanAsAdminAsync(victimId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", victimAccess);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Unban_Allows_New_Login()
    {
        ClearAuth();
        var email = $"unban-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-password-123";
        var access = await RegisterAndLoginAsync(email, password);
        var userId = await FindUserIdAsync(email);

        await BanAsAdminAsync(userId);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        (await _client.PostAsync("/auth/logout-all", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await UnbanAsAdminAsync(userId);

        ClearAuth();
        var login = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Admin_Cannot_Ban_Self()
    {
        ClearAuth();
        var adminId = await FindUserIdAsync("admin@example.com");
        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var ban = await _client.PostAsync($"/auth/admin/users/{adminId}/ban", null);
        ban.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Logout ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logout_Revokes_Refresh_Family()
    {
        ClearAuth();
        var email = $"logout-{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/auth/register", new { email, password = "A-long-password-123" });
        var login = await LoginFullAsync(email, "A-long-password-123");

        var logout = await _client.PostAsJsonAsync("/auth/logout", new { refreshToken = login.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var refresh = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = login.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task LogoutAll_Invalidates_Access_And_Refresh()
    {
        ClearAuth();
        var email = $"logoutall-{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/auth/register", new { email, password = "A-long-password-123" });
        var login = await LoginFullAsync(email, "A-long-password-123");

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var logoutAll = await _client.PostAsync("/auth/logout-all", null);
        logoutAll.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Old access JWT still signed but token_version mismatch → 401
        (await _client.PostAsync("/auth/logout-all", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        ClearAuth();
        var refresh = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken = login.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Register / rate limit / tx semantics ──────────────────────────────

    [Fact]
    public async Task Register_Works_Outside_Development()
    {
        ClearAuth();
        var email = $"reg-{Guid.NewGuid():N}@example.com";
        var response = await _client.PostAsJsonAsync("/auth/register", new
        {
            email,
            password = "A-long-password-123",
        });
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Register_Duplicate_Email_Returns_Conflict()
    {
        ClearAuth();
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/auth/register", new { email, password = "A-long-password-123" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var dup = await _client.PostAsJsonAsync("/auth/register", new { email, password = "A-long-password-123" });
        dup.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var problem = await ReadProblemAsync(dup);
        problem.GetProperty("errorCode").GetString().Should().BeOneOf("AUTH.EMAIL_TAKEN", "DB.CONFLICT");
    }

    [Fact]
    public async Task Auth_RateLimit_Returns_429_With_RetryAfter()
    {
        await using var factory = MyAppFactory.WithConfig(new Dictionary<string, string?>
        {
            ["RateLimit:AuthPerMinute"] = "3",
            ["RateLimit:GlobalPerMinute"] = "10000",
        });
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();

        var client = factory.CreateClient();

        HttpResponseMessage? last = null;
        for (var i = 0; i < 10; i++)
        {
            last = await client.PostAsJsonAsync("/auth/login", new
            {
                email = "nobody@example.com",
                password = "x",
            });
            if (last.StatusCode == HttpStatusCode.TooManyRequests)
                break;
        }

        last!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        last.Headers.TryGetValues("Retry-After", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Failed_CreateProduct_Does_Not_Persist()
    {
        ClearAuth();
        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var before = await CountProductsAsync();
        var response = await _client.PostAsJsonAsync("/products", new
        {
            Name = "ab",
            Category = "x",
            Price = 0,
            Stock = -1,
        });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CountProductsAsync()).Should().Be(before);
    }

    // ── Roles management ──────────────────────────────────────────────────

    [Fact]
    public async Task Roles_NonAdmin_Gets_403()
    {
        ClearAuth();
        var userToken = await RegisterAndLoginAsync($"norole-{Guid.NewGuid():N}@example.com", "A-long-password-123");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userToken);
        (await _client.GetAsync("/auth/admin/roles")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Roles_Admin_Lists_And_Creates()
    {
        ClearAuth();
        await AsAdminAsync();

        var list = await _client.GetAsync("/auth/admin/roles");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var roles = await list.Content.ReadFromJsonAsync<List<RoleDto>>(Json);
        roles!.Should().Contain(r => r.Name == Roles.Admin);
        roles.Should().Contain(r => r.Name == Roles.User);

        var name = $"Ed{Guid.NewGuid():N}";
        var create = await _client.PostAsJsonAsync("/auth/admin/roles", new { name });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await create.Content.ReadFromJsonAsync<RoleDto>(Json);
        created!.Name.Should().Be(name);

        var list2 = await _client.GetFromJsonAsync<List<RoleDto>>("/auth/admin/roles", Json);
        list2!.Should().Contain(r => r.Id == created.Id);
    }

    [Fact]
    public async Task Assign_Admin_Role_Then_ReLogin_Can_Access_Admin()
    {
        ClearAuth();
        var email = $"promote-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-password-123";
        var access = await RegisterAndLoginAsync(email, password);
        var userId = await FindUserIdAsync(email);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        await AsAdminAsync();
        var assign = await _client.PostAsync(
            $"/auth/admin/users/{userId}/roles/{RoleConfiguration.AdminRoleId}", null);
        assign.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Old JWT invalidated via PermissionVersion
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        ClearAuth();
        var newAccess = await LoginAsync(email, password);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccess);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Remove_Admin_Role_Old_Jwt_401_Then_ReLogin_403()
    {
        ClearAuth();
        var email = $"demote-{Guid.NewGuid():N}@example.com";
        const string password = "A-long-password-123";
        await RegisterAndLoginAsync(email, password);
        var userId = await FindUserIdAsync(email);

        await AsAdminAsync();
        (await _client.PostAsync(
            $"/auth/admin/users/{userId}/roles/{RoleConfiguration.AdminRoleId}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        ClearAuth();
        var elevated = await LoginAsync(email, password);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", elevated);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.OK);

        await AsAdminAsync();
        (await _client.DeleteAsync(
            $"/auth/admin/users/{userId}/roles/{RoleConfiguration.AdminRoleId}"))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", elevated);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        ClearAuth();
        var demoted = await LoginAsync(email, password);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", demoted);
        (await _client.GetAsync("/auth/admin/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Delete_Seeded_Admin_Role_Forbidden()
    {
        ClearAuth();
        await AsAdminAsync();
        var del = await _client.DeleteAsync($"/auth/admin/roles/{RoleConfiguration.AdminRoleId}");
        del.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(del)).GetProperty("errorCode").GetString().Should().Be("AUTH.ROLE_PROTECTED");
    }

    [Fact]
    public async Task Delete_Custom_Role_With_Members_Conflict()
    {
        ClearAuth();
        await AsAdminAsync();
        var name = $"Tmp{Guid.NewGuid():N}";
        var create = await _client.PostAsJsonAsync("/auth/admin/roles", new { name });
        create.EnsureSuccessStatusCode();
        var role = await create.Content.ReadFromJsonAsync<RoleDto>(Json);

        var email = $"member-{Guid.NewGuid():N}@example.com";
        await RegisterAndLoginAsync(email, "A-long-password-123");
        var userId = await FindUserIdAsync(email);

        await AsAdminAsync();
        (await _client.PostAsync($"/auth/admin/users/{userId}/roles/{role!.Id}", null))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        var del = await _client.DeleteAsync($"/auth/admin/roles/{role.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReadProblemAsync(del)).GetProperty("errorCode").GetString().Should().Be("AUTH.ROLE_IN_USE");
    }

    // ── helpers ───────────────────────────────────────────────────────────

    private async Task AsAdminAsync()
    {
        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
    }

    private async Task BanAsAdminAsync(Guid userId)
    {
        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var ban = await _client.PostAsync($"/auth/admin/users/{userId}/ban", null);
        ban.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ClearAuth();
    }

    private async Task UnbanAsAdminAsync(Guid userId)
    {
        var adminToken = await LoginAsync("admin@example.com", "ChangeMe-12345");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        var unban = await _client.PostAsync($"/auth/admin/users/{userId}/unban", null);
        unban.StatusCode.Should().Be(HttpStatusCode.NoContent);
        ClearAuth();
    }

    private async Task<Guid> FindUserIdAsync(string email)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users
            .Where(u => u.NormalizedEmail == email.ToUpperInvariant())
            .Select(u => u.Id)
            .SingleAsync();
    }

    private async Task<int> CountProductsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Products.CountAsync();
    }

    private async Task<string> RegisterAndLoginAsync(string email, string password)
    {
        var reg = await _client.PostAsJsonAsync("/auth/register", new { email, password });
        reg.EnsureSuccessStatusCode();
        return await LoginAsync(email, password);
    }

    private async Task<string> LoginAsync(string email, string password)
    {
        var full = await LoginFullAsync(email, password);
        return full.AccessToken;
    }

    private async Task<TokenDto> LoginFullAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenDto>(Json))!;
    }

    private async Task<TokenDto> RefreshAsync(string refreshToken)
    {
        var response = await _client.PostAsJsonAsync("/auth/refresh", new { refreshToken });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TokenDto>(Json))!;
    }

    private static async Task<JsonElement> ReadProblemAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        return doc.RootElement.Clone();
    }

    private sealed record TokenDto(
        [property: System.Text.Json.Serialization.JsonPropertyName("access_token")] string AccessToken,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_type")] string TokenType,
        [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")] int ExpiresIn,
        [property: System.Text.Json.Serialization.JsonPropertyName("refresh_token")] string RefreshToken);

    private sealed record RoleDto(Guid Id, string Name, string NormalizedName);
}
