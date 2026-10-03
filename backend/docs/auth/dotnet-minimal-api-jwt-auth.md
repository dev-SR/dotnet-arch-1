# Production-Ready JWT Auth for MyApp.Api (custom IAM)
### Instant ban/revoke · refresh rotation · rate limiting · `.env` + Options · Scalar Bearer

Most JWT tutorials stop at "sign a token, validate a token." Real APIs also need: *What happens when I ban a user right now? What if a refresh token leaks? What if someone hammers `/auth/login`?*

This guide targets **[`MyApp.Api`](../../src/Services/MyApp.Api/)** and follows the **same feature pattern as Products**:

```
Command/Query → FluentValidation → Handler (ErrorOr) → Carter MatchOk / MatchNoContent
```

via [`Shared.Mediator`](../../src/Shared/Mediator), [`Shared.Common.Errors`](../../src/Shared/Common/Errors), and [`ValidationBehavior`](../../src/Shared/Application/Behaviors/ValidationBehavior.cs). IAM is **custom** (`User` / `Role` tables) — future-compatible with [`arch.test/docs/SECURITY.md`](../../../../arch.test/docs/SECURITY.md), not ASP.NET Core Identity.

**What this guide covers**

- Products-shaped vertical slices (Command + Validator + Handler + Endpoint)
- Short-lived JWT access tokens (HS256) + hashed, **rotating refresh tokens** with reuse detection
- **Immediate ban / sign-out-everywhere** via `PermissionVersion` + `token_version` claim
- Rate limiting + per-account lockout fields on `User`
- `.env` + Options; Scalar Bearer (paste token from `/auth/login`)
- Secure-by-default fallback authorization policy

**Not in this guide** (Phase 2 — see [end](#phase-2--bridge-to-archtest)): social OAuth, Next.js BFF, JWKS/RS256, fine-grained `perm:*` policies, multi-service client-credentials.

> **Status vs code today.** Implemented in [`MyApp.Api`](../../src/Services/MyApp.Api/) and covered by [`AuthSecurityTests`](../../tests/MyApp.Tests.Integration/AuthSecurityTests.cs). Samples below match the live code; do not add `IdentityDbContext`.

---

## Why not ASP.NET Core Identity (reevaluation)

| Type / feature | Use? | Why |
|---|---|---|
| `IdentityUser<TKey>` | **No** | Locks you into AspNet* + `UserManager`. Custom `User` keeps `PermissionVersion`, providers, service accounts later. |
| `IdentityRole<TKey>` | **No** | Roles as our DB rows; later `Permission` / `RolePermission` without Identity’s claim model. |
| `IdentityUserClaim` / `IdentityRoleClaim` | **No** | Permissions materialize into JWT at issue time (Phase 2). |
| `IdentityUserRole` | **No** | Replaced by `UserRole`. |
| `IdentityUserLogin` | **No for now** | Google/GitHub later (Phase 2). |
| `IdentityUserToken` | **No** | We own `RefreshToken` (+ handoff codes later). |
| `AddIdentity` / `AddIdentityCore` | **No** | Cookie/`SignInManager` stack is wrong for a bearer API we own. |
| `PasswordHasher<T>` only | **Yes** | PBKDF2 without Identity as IAM. |
| Identity lockout | **No** | `AccessFailedCount` + `LockoutEnd` on `User`. Lockout must not kill live bearer sessions. Ban = `IsBanned` + `PermissionVersion`. |

---

## How auth fits the Products pipeline

Same as [`CreateProduct`](../../src/Services/MyApp.Api/Features/Products/CreateProduct/CreateProductEndpoint.cs):

```
Carter Endpoint
  → IMediator.Send(ICommand|IQuery)
  → ValidationBehavior (FluentValidation → Error.ValidationField)
  → Handler → ErrorOr<T>  (business failures: Unauthorized, Forbidden, Conflict, …)
  → MatchOk / MatchNoContent / MatchCreated / ToProblem
       ([ErrorOrExtensions](../../src/Shared/Common/Errors/Http/ErrorOrExtensions.cs))
```

| Layer | Input rules (email shape, password length) | Business rules (wrong password, ban, duplicate email) |
|-------|--------------------------------------------|------------------------------------------------------|
| `*Validator` | Yes | No |
| Handler | No | Yes — `Error.*` factories |
| Endpoint | No | No — only `Send` + `Match*` |

**Do not** invent `AuthResult` / `AuthError`, call an `AuthService` from Carter, or hand-roll `result.Match(onValue, onError)` with OAuth error JSON. Failures are Problem Details like Products.

### MyApp pattern checklist (auth samples must follow)

| Concern | Use |
|---------|-----|
| Use-case shape | Command + Validator + Handler + Endpoint (Products) |
| Input rules | FluentValidation + `ValidationBehavior` |
| Business failures | `ErrorOr` + `Error.*` → `MatchOk` / `MatchNoContent` / `ToProblem` |
| JwtBearer 401/403 | `Error.*` + `ToProblem` → `ExecuteAsync` in events (**not** `ErrorOr`) |
| Queries with filters/Includes | Atomic filter + include specs + `.And()` + `Apply` (like ProductSpecs); mutations add `ForUpdateSpecification<T>`; `ExecuteUpdate` uses filter specs only (no Includes) |
| Persistence UoW | `TransactionBehavior` via `ICommandMarker`; commit on ErrorOr only if `IPersistOnFailure` |
| Composition | Thin `Program` → `AddApplication` / `AddInfrastructure` / `AddApi` |
| Error codes | Dotted style like Products (`AUTH.INVALID_CREDENTIALS`) |

Cite: [`SpecificationBase`](../../src/Shared/Common/Specifications/SpecificationBase.cs), [`ForUpdateSpecification`](../../src/Shared/Common/Specifications/ForUpdateSpecification.cs), [`Apply`](../../src/Shared/Common/Specifications/SpecificationEvaluator.cs), [`ProductSpecs`](../../src/Services/MyApp.Api/Features/Products/Specifications/ProductSpecs.cs), [`ErrorOrExtensions`](../../src/Shared/Common/Errors/Http/ErrorOrExtensions.cs), [`ErrorExtensions.ToProblem`](../../src/Shared/Common/Errors/Http/ErrorExtensions.cs).

---

## The design in one table

| Token | Format | Lifetime | Stored | Killed by |
|---|---|---|---|---|
| Access | JWT (HS256) | 10 min | Client only (or BFF later) | Expiry, or per-request ban / `token_version` mismatch |
| Refresh | Random 64 bytes | 7 days | **SHA-256 hash** in DB | Rotation, logout, ban, reuse detection |

**Immediate revocation:** on each authenticated request, one PK lookup — banned? or `token_version` ≠ `User.PermissionVersion`? Ban / sign-out-everywhere bumps `PermissionVersion` and revokes refresh tokens.

> **Banned ≠ lockout.** Lockout is failed-login throttling. Bearer validation checks `IsBanned` / version, not lockout — an attacker must not log out a victim by failing logins.

```
POST /auth/login → LoginCommand → TokenResponse
POST /auth/refresh → RefreshTokenCommand → TokenResponse
   │ JWT issued with token_version = user.PermissionVersion
   ▼
OnTokenValidated: IsBanned? / token_version mismatch? → Fail → OnChallenge → Problem Details
Other API calls: bad/expired/missing JWT → OnChallenge; policy deny → OnForbidden
```

---

## Step 1: Project layout and packages

### 1.1 Folder layout (create these Auth files as you follow later steps)

```
MyApp/backend/src/Services/MyApp.Api/
├── Program.cs
├── Config/Options.cs
├── Features/Auth/
│   ├── Entities.cs
│   ├── AuthConstants.cs
│   ├── ITokenService.cs
│   ├── TokenService.cs
│   ├── IssuedTokens.cs
│   ├── TokenResponse.cs
│   ├── ConfigureJwtBearer.cs
│   ├── JwtBearerProblem.cs
│   ├── Specifications/
│   │   └── AuthSpecs.cs
│   ├── RegisterUser/
│   │   ├── RegisterUserCommand.cs
│   │   ├── RegisterUserCommandValidator.cs
│   │   ├── RegisterUserCommandHandler.cs
│   │   └── RegisterUserEndpoint.cs
│   ├── Login/
│   │   ├── LoginCommand.cs
│   │   ├── LoginCommandValidator.cs
│   │   ├── LoginCommandHandler.cs
│   │   └── LoginEndpoint.cs
│   ├── RefreshToken/
│   │   ├── RefreshTokenCommand.cs
│   │   ├── RefreshTokenCommandValidator.cs
│   │   ├── RefreshTokenCommandHandler.cs
│   │   └── RefreshTokenEndpoint.cs
│   ├── Logout/  LogoutAll/  Me/  AdminUsers/
├── Persistence/
│   ├── AppDbContext.cs
│   └── Configurations/
│       ├── UserConfiguration.cs
│       ├── RoleConfiguration.cs
│       ├── UserRoleConfiguration.cs
│       └── RefreshTokenConfiguration.cs
├── Application/DependencyInjection.cs
├── Infrastructure/DependencyInjection.cs
└── Presentation/
    ├── DependencyInjection.cs
    └── WebApplicationExtensions.cs
```

### 1.2 Packages

Ensure the project file includes JwtBearer, DotNetEnv, EF, Carter, FluentValidation, and **Identity.Core** (for `PasswordHasher<T>` only — do **not** add `Identity.EntityFrameworkCore` or call `AddIdentityCore`):

```xml title="MyApp/backend/src/Services/MyApp.Api/MyApp.Api.csproj"
<Project Sdk="Microsoft.NET.Sdk.Web">

    <PropertyGroup>
        <TargetFramework>net10.0</TargetFramework>
        <RootNamespace>MyApp</RootNamespace>
        <AssemblyName>MyApp.Api</AssemblyName>
        <IsPackable>false</IsPackable>
        <UserSecretsId>ecomapi-local-dev</UserSecretsId>
    </PropertyGroup>

    <ItemGroup>
        <PackageReference Include="Bogus" />
        <PackageReference Include="DotNetEnv" />
        <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" />
        <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
        <PackageReference Include="Microsoft.Extensions.Identity.Core" />
        <PackageReference Include="Scalar.AspNetCore" />

        <PackageReference Include="Microsoft.EntityFrameworkCore" />
        <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
        <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
            <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
            <PrivateAssets>all</PrivateAssets>
        </PackageReference>

        <PackageReference Include="FluentValidation.AspNetCore" />
        <PackageReference Include="Asp.Versioning.Mvc" />
        <PackageReference Include="Asp.Versioning.Mvc.ApiExplorer" />
        <PackageReference Include="Carter" />
        <PackageReference Include="System.Linq.Dynamic.Core" />
    </ItemGroup>

    <ItemGroup>
      <ProjectReference Include="..\..\Shared\Mediator\Mediator.csproj" />
      <ProjectReference Include="..\..\Shared\Application\Shared.Application.csproj" />
      <ProjectReference Include="..\..\Shared\Common\Shared.Common.csproj" />
    </ItemGroup>

</Project>
```

---

## Step 2: Configuration with `.env` and the Options pattern

### 2.1 Options classes

```csharp title="MyApp/backend/src/Services/MyApp.Api/Config/Options.cs"
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace MyApp.Config;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    [Required, MinLength(32)] public string Secret { get; set; } = "";
    [Required] public string Issuer { get; set; } = "";
    [Required] public string Audience { get; set; } = "";
    [Range(1, 60)] public int AccessTokenMinutes { get; set; } = 10;
    [Range(1, 90)] public int RefreshTokenDays { get; set; } = 7;
}

public sealed class RateLimitOptions
{
    public const string Section = "RateLimit";

    [Range(1, 10_000)] public int GlobalPerMinute { get; set; } = 120;
    [Range(1, 100)] public int AuthPerMinute { get; set; } = 5;
}

public sealed class SeedOptions
{
    public const string Section = "Seed";

    public string? AdminEmail { get; set; }
    public string? AdminPassword { get; set; }
}

public static class OptionsExtensions
{
    /// Bind + validate + fail fast at startup (a weak or missing secret should crash the app, not ship).
    public static OptionsBuilder<T> AddValidatedOptions<T>(this IServiceCollection services, string section)
        where T : class =>
        services.AddOptions<T>()
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
}
```

### 2.2 `.env` (repo / Api working directory)

```ini title="MyApp/backend/src/Services/MyApp.Api/.env"
Jwt__Secret=REPLACE_WITH_A_LONG_RANDOM_SECRET_AT_LEAST_32_CHARS
Jwt__Issuer=myapp-api
Jwt__Audience=myapp-clients
Jwt__AccessTokenMinutes=10
Jwt__RefreshTokenDays=7
ConnectionStrings__Default=Data Source=app.db
RateLimit__GlobalPerMinute=120
RateLimit__AuthPerMinute=5
Seed__AdminEmail=admin@example.com
Seed__AdminPassword=ChangeMe-12345
```

Generate secret: `openssl rand -base64 48`.

### 2.3 Load DotNetEnv before `CreateBuilder`

Double-underscore keys become nested configuration (`Jwt:Secret`, …). Load `.env` **before** `WebApplication.CreateBuilder`:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Program.cs"
using DotNetEnv;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Presentation;

Env.TraversePath().Load(); // before CreateBuilder so Jwt__* bind into IConfiguration

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseInfrastructure()
    .UsePresentation();

app.Run();

public partial class Program;
```

Register validated options in Infrastructure **when you first need them** (Jwt for Steps 4/6, RateLimit for Step 8, Seed for migrations/seed):

```csharp
services.AddValidatedOptions<JwtOptions>(JwtOptions.Section);
services.AddValidatedOptions<RateLimitOptions>(RateLimitOptions.Section);
services.AddValidatedOptions<SeedOptions>(SeedOptions.Section);
```

---

## Step 3: Data model (custom IAM)

### 3.1 Entities

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Entities.cs"
namespace MyApp.Features.Auth;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsBanned { get; set; }
    /// <summary>Bumped on ban / sign-out-everywhere. JWT claim <c>token_version</c>.</summary>
    public int PermissionVersion { get; set; } = 1;
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<UserRole> UserRoles { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

public sealed class Role
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public List<UserRole> UserRoles { get; set; } = [];
}

public sealed class UserRole
{
    public Guid UserId { get; init; }
    public User User { get; set; } = null!;
    public Guid RoleId { get; init; }
    public Role Role { get; set; } = null!;
}

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public User User { get; set; } = null!;
    public required string TokenHash { get; init; }
    public Guid FamilyId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; set; }
}
```

### 3.2 EF configurations

```csharp title="MyApp/backend/src/Services/MyApp.Api/Persistence/Configurations/UserConfiguration.cs"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordHash).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.HasMany(x => x.UserRoles).WithOne(x => x.User).HasForeignKey(x => x.UserId);
        b.HasMany(x => x.RefreshTokens).WithOne(x => x.User).HasForeignKey(x => x.UserId);
    }
}
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Persistence/Configurations/RoleConfiguration.cs"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    // Fixed GUIDs so HasData is stable across every environment (not Dev-only seeding).
    public static readonly Guid UserRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.NormalizedName).IsUnique();
        b.HasMany(x => x.UserRoles).WithOne(x => x.Role).HasForeignKey(x => x.RoleId);

        b.HasData(
            new Role { Id = UserRoleId, Name = Roles.User, NormalizedName = "USER" },
            new Role { Id = AdminRoleId, Name = Roles.Admin, NormalizedName = "ADMIN" });
    }
}
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Persistence/Configurations/UserRoleConfiguration.cs"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> b)
    {
        b.HasKey(x => new { x.UserId, x.RoleId });
    }
}
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Persistence/Configurations/RefreshTokenConfiguration.cs"
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.TokenHash).IsUnique();
        b.HasIndex(x => x.FamilyId);
        b.HasIndex(x => x.UserId);
        b.HasOne(x => x.User).WithMany(u => u.RefreshTokens).HasForeignKey(x => x.UserId);
    }
}
```

### 3.3 AppDbContext

Plain `DbContext` (not `IdentityDbContext`). Discover configs via `ApplyConfigurationsFromAssembly`:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Persistence/AppDbContext.cs"
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth;
using MyApp.Features.Brands;
using MyApp.Features.Categories;
using MyApp.Features.Customers;
using MyApp.Features.Orders;
using MyApp.Features.Products;
using MyApp.Features.Reviews;
using MyApp.Features.Tags;

namespace MyApp.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

### 3.4 Migrations and seed — do this now

Roles `User` / `Admin` already ship via `RoleConfiguration.HasData` (stable GUIDs) — they apply with the migration, not a Dev-only seeder.

**Add and apply the Auth migration** (from the backend repo root):

```bash
dotnet ef migrations add Auth \
  --project src/Services/MyApp.Api \
  --output-dir Persistence/Migrations

dotnet ef database update --project src/Services/MyApp.Api
```

**Optional admin user** from `SeedOptions` (`.env` in Step 2). Register options + hasher, then migrate/seed on startup:

```csharp
// Infrastructure DI (AddInfrastructure)
services.AddValidatedOptions<SeedOptions>(SeedOptions.Section);
services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Infrastructure/WebApplicationExtensions.cs"
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyApp.Config;
using MyApp.Features.Auth;
using MyApp.Persistence;
using MyApp.Persistence.Configurations;
using MyApp.Persistence.Seeding;

namespace MyApp.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();

        if (db.Database.IsRelational())
            db.Database.Migrate();
        else
            db.Database.EnsureCreated();

        SeedAdminIfConfiguredAsync(sp, db).GetAwaiter().GetResult();

        if (app.Environment.IsDevelopment())
            DataSeeder.SeedAsync(db).GetAwaiter().GetResult(); // products demo data — unrelated to IAM

        return app;
    }

    private static async Task SeedAdminIfConfiguredAsync(IServiceProvider sp, AppDbContext db)
    {
        var seed = sp.GetRequiredService<IOptions<SeedOptions>>().Value;
        if (string.IsNullOrWhiteSpace(seed.AdminEmail) || string.IsNullOrWhiteSpace(seed.AdminPassword))
            return;

        var normalized = seed.AdminEmail.Trim().ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalized))
            return;

        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();
        var user = new User
        {
            Email = seed.AdminEmail.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = "",
        };
        user.PasswordHash = hasher.HashPassword(user, seed.AdminPassword);
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleConfiguration.AdminRoleId });
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleConfiguration.UserRoleId });
        await db.SaveChangesAsync();
    }
}
```

Call `app.UseInfrastructure()` from `Program` after `Build()` (thin host — full `Program.cs` assembly is Step 10).

---

## Step 4: Constants and ITokenService

### 4.1 Auth constants

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/AuthConstants.cs"
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MyApp.Features.Auth;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
}

public static class AuthClaims
{
    public const string Role = "role";
    public const string TokenVersion = "token_version";

    public static Guid GetUserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
```

### 4.2 Token response DTO

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/TokenResponse.cs"
using System.Text.Json.Serialization;

namespace MyApp.Features.Auth;

public sealed record TokenResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("refresh_token")] string RefreshToken);
```

### 4.3 Token service (interface + implementation)

Handlers depend on **`ITokenService`**, not the concrete class.

**Why no `AppDbContext` on `IssueTokens`?** Token issuance is pure crypto + entity construction. Persisting the refresh row is the handler’s job (`db.RefreshTokens.Add`), so `TransactionBehavior` owns the unit of work and `IPersistOnFailure` / rollback semantics stay predictable.

**Why not return `TokenResponse`?** That record is the HTTP/API contract (`access_token`, `token_type`, …). `TokenService` owns generation only; the handler maps `IssuedTokens` → `TokenResponse` (including `"Bearer"`).

**Usage in a handler:**

```csharp
var issued = tokens.IssueTokens(user, roles, familyId);
db.RefreshTokens.Add(issued.RefreshToken);   // tracked; SaveChanges via TransactionBehavior
return new TokenResponse(
    issued.AccessToken,
    "Bearer",
    issued.AccessTokenExpiresInSeconds,
    issued.RawRefreshToken);
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/IssuedTokens.cs"
namespace MyApp.Features.Auth;

/// <summary>
/// Domain result of token issuance. <see cref="RawRefreshToken"/> is the one-time
/// client secret; <see cref="RefreshToken"/> is the DB entity (hash only).
/// </summary>
public sealed record IssuedTokens(
    string AccessToken,
    int AccessTokenExpiresInSeconds,
    string RawRefreshToken,
    RefreshToken RefreshToken);
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/ITokenService.cs"
namespace MyApp.Features.Auth;

public interface ITokenService
{
    string CreateAccessToken(User user, IEnumerable<string> roles);
    (string Raw, string Hash, DateTime ExpiresAt) GenerateRefreshToken();
    string Hash(string raw);
    int AccessTokenExpiresInSeconds { get; }

    /// <summary>
    /// Creates access + refresh entity (caller Adds the entity and maps to TokenResponse;
    /// SaveChanges via TransactionBehavior).
    /// </summary>
    IssuedTokens IssueTokens(User user, IEnumerable<string> roles, Guid familyId);
}
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/TokenService.cs"
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApp.Config;

namespace MyApp.Features.Auth;

public sealed class TokenService : ITokenService
{
    private readonly JwtOptions _jwt;
    private readonly TimeProvider _clock;
    private readonly SigningCredentials _creds;

    public TokenService(IOptions<JwtOptions> jwt, TimeProvider clock)
    {
        _jwt = jwt.Value;
        _clock = clock;
        // Cache key + credentials once — do not rebuild per request.
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Secret));
        _creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    }

    public string CreateAccessToken(User user, IEnumerable<string> roles)
    {
        var now = _clock.GetUtcNow();
        var expires = now.AddMinutes(_jwt.AccessTokenMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new(JwtRegisteredClaimNames.Iat, EpochTime.GetIntDate(now.UtcDateTime).ToString(), ClaimValueTypes.Integer64),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(AuthClaims.TokenVersion, user.PermissionVersion.ToString()),
        };
        // Short name "role" — JwtBearer RoleClaimType must match (see Step 6).
        claims.AddRange(roles.Select(r => new Claim(AuthClaims.Role, r)));

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = _creds,
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    public (string Raw, string Hash, DateTime ExpiresAt) GenerateRefreshToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var raw = Base64UrlEncoder.Encode(bytes); // URL-safe; avoid + / =
        var expires = _clock.GetUtcNow().UtcDateTime.AddDays(_jwt.RefreshTokenDays);
        return (raw, Hash(raw), expires);
    }

    public string Hash(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public int AccessTokenExpiresInSeconds => _jwt.AccessTokenMinutes * 60;

    public IssuedTokens IssueTokens(
        User user, IEnumerable<string> roles, Guid familyId)
    {
        var accessToken = CreateAccessToken(user, roles);
        var (rawRefreshToken, hash, expiresAt) = GenerateRefreshToken();
        var now = _clock.GetUtcNow().UtcDateTime;

        var refreshToken = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,       // store hash only — never the raw token
            FamilyId = familyId,    // same family across rotations; reuse kills the family
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };

        return new IssuedTokens(
            accessToken,
            AccessTokenExpiresInSeconds,
            rawRefreshToken,
            refreshToken);
    }
}
```

---

## Step 5: Vertical slices (Command + Validator + Handler + Endpoint)

Mirror Products — each use case is one folder with **Command, Validator, Handler, and Carter Endpoint**. Handlers return `ValueTask<ErrorOr<T>>`. Input validation is **only** in FluentValidation. Endpoints only `Send` + `Match*`. `SaveChanges` follows [`TransactionBehavior`](../../src/Services/MyApp.Api/Persistence/TransactionBehavior.cs).

#### How `TransactionBehavior` decides to commit

| Request implements | Handler returns | Commit? | Typical use |
|---|---|---|---|
| `IQuery<T>` (no `ICommandMarker`) | anything | N/A — no transaction | Reads |
| `ICommand` / `ICommand<T>` | success `ErrorOr` | **Yes** | CreateProduct, Register, Ban |
| `ICommand` / `ICommand<T>` | `IsError: true` | **No** (rollback) | Duplicate email, validation already stopped earlier |
| `ICommand` + **`IPersistOnFailure`** | `IsError: true` | **Yes** | Login lockout, refresh family kill on reuse |

**Why `IPersistOnFailure` exists:** lockout counters and “kill the refresh family” must hit the DB even though the HTTP response is still `401`. Without the marker, `TransactionBehavior` would roll those `ExecuteUpdate`s back with the ErrorOr failure.

**Usage — mark only the commands that need it:**

```csharp
public sealed record LoginCommand(string Email, string Password)
    : ICommand<TokenResponse>, IPersistOnFailure;

public sealed record RefreshTokenCommand(string RefreshToken)
    : ICommand<TokenResponse>, IPersistOnFailure;

// Ordinary commands — failed ErrorOr rolls back; do NOT add IPersistOnFailure.
public sealed record CreateProductCommand(...) : ICommand<Guid>;
```

Other rules:

- Commands are detected via [`ICommandMarker`](../../src/Shared/Application/Abstractions/Commands/ICommand.cs) (`ICommand` / `ICommand<T>` inherit it) — never `EndsWith("Query")`.
- Unique violations → `DB.CONFLICT` (409). `DbUpdateConcurrencyException` → `CONCURRENCY_CONFLICT` in [`ExceptionMapping`](../../src/Shared/Common/Exceptions/Http/ExceptionMapping.cs).

### 5.0 Specifications for auth reads (compose like Products)

Same idea as [`ProductSpecs`](../../src/Services/MyApp.Api/Features/Products/Specifications/ProductSpecs.cs) + [`GetProductsQueryHandler`](../../src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsQueryHandler.cs):

- **Filter specs** — Criteria only (reusable everywhere: `AnyAsync`, `SingleAsync`, bearer checks).
- **Include specs** — graph only; `.And()` onto a filter when the handler needs navigations.
- **`ForUpdateSpecification<T>`** — flips tracking on via `.And(...)` when the handler mutates (`AsNoTracking` on combined specs is AND of both sides).

Do **not** bake Includes + tracking into one fat “Login-only” spec.

```csharp title="MyApp/backend/src/Shared/Common/Specifications/ForUpdateSpecification.cs"
namespace Shared.Common.Specifications;

/// <summary>
/// Marks a composed query as tracked so the handler can mutate entities.
/// Combine with filter/include specs via <c>.And(ForUpdateSpecification&lt;T&gt;.Instance)</c>.
/// Combined <see cref="ISpecification{T}.AsNoTracking"/> is AND of both sides, so this flips tracking on.
/// </summary>
public sealed class ForUpdateSpecification<T> : SpecificationBase<T> where T : class
{
    private ForUpdateSpecification() => AsNoTracking = false;

    public static readonly ISpecification<T> Instance = new ForUpdateSpecification<T>();
}
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Specifications/AuthSpecs.cs"
using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.Specifications;

// Atomic Criteria filters — compose with .And() like ProductSpecs.
// Default AsNoTracking = true. Mutating handlers add ForUpdateSpecification<T>.Instance.
// ExecuteUpdate paths also .Apply(filter specs) — Criteria only, never Includes.

public sealed class UserByIdSpecification : SpecificationBase<User>
{
    public UserByIdSpecification(Guid userId) => Where(u => u.Id == userId);
}

public sealed class UserByNormalizedEmailSpecification : SpecificationBase<User>
{
    public UserByNormalizedEmailSpecification(string normalizedEmail) =>
        Where(u => u.NormalizedEmail == normalizedEmail);
}

public sealed class UserWithMinAccessFailedCountSpecification : SpecificationBase<User>
{
    public UserWithMinAccessFailedCountSpecification(int minFailed) =>
        Where(u => u.AccessFailedCount >= minFailed);
}

public sealed class RoleByIdSpecification : SpecificationBase<Role>
{
    public RoleByIdSpecification(Guid roleId) => Where(r => r.Id == roleId);
}

public sealed class RoleByNormalizedNameSpecification : SpecificationBase<Role>
{
    public RoleByNormalizedNameSpecification(string normalizedName) =>
        Where(r => r.NormalizedName == normalizedName);
}

public sealed class RefreshTokenByHashSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByHashSpecification(string tokenHash) =>
        Where(t => t.TokenHash == tokenHash);
}

public sealed class RefreshTokenByIdSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByIdSpecification(Guid id) => Where(t => t.Id == id);
}

public sealed class RefreshTokenByFamilyIdSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByFamilyIdSpecification(Guid familyId) =>
        Where(t => t.FamilyId == familyId);
}

public sealed class RefreshTokenByUserIdSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByUserIdSpecification(Guid userId) =>
        Where(t => t.UserId == userId);
}

/// <summary>Not yet revoked — compose with id / family / user filters for ExecuteUpdate revoke.</summary>
public sealed class ActiveRefreshTokenSpecification : SpecificationBase<RefreshToken>
{
    public ActiveRefreshTokenSpecification() => Where(t => t.RevokedAt == null);

    public static readonly ISpecification<RefreshToken> Instance = new ActiveRefreshTokenSpecification();
}

public sealed class UserRolesByUserIdSpecification : SpecificationBase<UserRole>
{
    public UserRolesByUserIdSpecification(Guid userId) => Where(ur => ur.UserId == userId);
}

public sealed class UserRoleByUserAndRoleSpecification : SpecificationBase<UserRole>
{
    public UserRoleByUserAndRoleSpecification(Guid userId, Guid roleId) =>
        Where(ur => ur.UserId == userId && ur.RoleId == roleId);
}

// Include graphs only — no Criteria. And() with a filter above.

public sealed class RefreshTokenWithUserRolesSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenWithUserRolesSpecification()
    {
        Include(q => q.Include(t => t.User)
            .ThenInclude(u => u!.UserRoles)
            .ThenInclude(ur => ur.Role));
    }

    public static readonly ISpecification<RefreshToken> Instance = new RefreshTokenWithUserRolesSpecification();
}
```

| Use case | Composition |
|----------|-------------|
| Register — email taken? | `UserByNormalizedEmailSpecification` → `.AnyAsync` |
| Register — default role | `RoleByNormalizedNameSpecification` → `.SingleAsync` |
| Login | email filter `.And(ForUpdate)`; roles via `UserRolesByUserIdSpecification` → `.Select(Name)` |
| Login — lockout bump | `UserById` → `ExecuteUpdate`; lock when `.And(UserWithMinAccessFailedCount)` |
| Refresh | hash filter `.And(RefreshTokenWithUserRoles)` → pass role names into `IssueTokens` |
| Refresh — rotate / reuse kill | `RefreshTokenById` / `ByFamilyId` `.And(ActiveRefreshToken)` → `ExecuteUpdate` |
| Ban / LogoutAll / assign-role | `UserById` `.And(ForUpdate)`; revoke via `ByUserId` `.And(ActiveRefreshToken)` → `ExecuteUpdate` |
| Logout | hash → family id; `ByFamilyId` `.And(ActiveRefreshToken)` → `ExecuteUpdate` |
| `OnTokenValidated` | `UserById` alone (no-tracking) |

`ExecuteUpdate` still uses **filter specs only** (Criteria) — never include graphs. `ForUpdate` is irrelevant for `ExecuteUpdate` (no materialization).

### 5.1 RegisterUser

**`RegisterUserCommand.cs`**

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/RegisterUser/RegisterUserCommand.cs"
using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.RegisterUser;

public sealed record RegisterUserCommand(string Email, string Password) : ICommand;
```

**`RegisterUserCommandValidator.cs`** — replaces any manual `ValidatePassword`:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/RegisterUser/RegisterUserCommandValidator.cs"
using FluentValidation;

namespace MyApp.Features.Auth.RegisterUser;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(10)
            .MaximumLength(128)
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
            .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
            .Matches("[^a-zA-Z0-9]").WithMessage("Password must contain a non-alphanumeric character.");
    }
}
```

**`RegisterUserCommandHandler.cs`**

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/RegisterUser/RegisterUserCommandHandler.cs"
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Errors;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.RegisterUser;

internal sealed class RegisterUserCommandHandler(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher)
    : IRequestHandler<RegisterUserCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        var normalized = command.Email.Trim().ToUpperInvariant();
        if (await db.Users
                .Apply(new UserByNormalizedEmailSpecification(normalized))
                .AnyAsync(ct))
            return Error.Conflict("AUTH.EMAIL_TAKEN", "Email is already taken.");

        var userRole = await db.Roles
            .Apply(new RoleByNormalizedNameSpecification(Roles.User.ToUpperInvariant()))
            .SingleAsync(ct);

        var user = new User
        {
            Email = command.Email.Trim(),
            NormalizedEmail = normalized,
            PasswordHash = "",
        };
        user.PasswordHash = passwordHasher.HashPassword(user, command.Password);

        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = userRole.Id });
        // SaveChanges via TransactionBehavior
        return default(Success);
    }
}
```

**`RegisterUserEndpoint.cs`** — same shape as CreateProduct; body binds the command:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/RegisterUser/RegisterUserEndpoint.cs"
using Shared.Common.Errors.Http;

namespace MyApp.Features.Auth.RegisterUser;

public sealed class RegisterUserEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", HandleAsync)
            .WithName("RegisterUser")
            .WithTags("Auth")
            .AllowAnonymous();
        // After Step 8: .RequireRateLimiting(ApiRateLimiting.AuthPolicy)
    }

    private static async Task<IResult> HandleAsync(
        RegisterUserCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.MatchNoContent(http); // or Match with 201 if you prefer Created
    }
}
```

### 5.2 Login (validation here)

**`LoginCommand.cs`**

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Login/LoginCommand.cs"
using Shared.Application.Abstractions.Commands;
using MyApp.Features.Auth;

namespace MyApp.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password)
    : ICommand<TokenResponse>, IPersistOnFailure;
```

**`LoginCommandValidator.cs`** — presence/shape only (complexity is register-only):

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Login/LoginCommandValidator.cs"
using FluentValidation;

namespace MyApp.Features.Auth.Login;

public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(128);
    }
}
```

**`LoginCommandHandler.cs`**

**Why `IPersistOnFailure`:** wrong-password paths return `Error.Unauthorized` *and* must persist `AccessFailedCount` / `LockoutEnd`. Default TransactionBehavior would roll those writes back.

**Why DummyHash on early exits:** missing / banned / locked users still run a password verify against a fixed hash so timing does not leak whether the email exists.

**Why `ExecuteUpdate` for failures (not tracked mutate):** lockout increments must not mix with a later failed `SaveChanges` of a tracked entity graph. Atomic SQL updates + `IPersistOnFailure` commit them.

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Login/LoginCommandHandler.cs"
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.Login;

internal sealed class LoginCommandHandler(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokens,
    TimeProvider clock)
    : IRequestHandler<LoginCommand, ErrorOr<TokenResponse>>
{
    private const int MaxFailed = 5;
    private static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);

    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(
        new User { Email = "x", NormalizedEmail = "X", PasswordHash = "" },
        Guid.NewGuid().ToString());

    public async ValueTask<ErrorOr<TokenResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var normalized = command.Email.Trim().ToUpperInvariant();
        var user = await db.Users
            .Apply(new UserByNormalizedEmailSpecification(normalized)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null || user.IsBanned)
        {
            BurnPasswordVerify(command.Password);
            return Error.Unauthorized("AUTH.INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (user.LockoutEnd is { } until && until > clock.GetUtcNow())
        {
            BurnPasswordVerify(command.Password);
            return Error.Unauthorized("AUTH.INVALID_CREDENTIALS", "Invalid credentials.");
        }

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            await db.Users
                .Apply(new UserByIdSpecification(user.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount + 1), ct);

            var lockUntil = clock.GetUtcNow().Add(LockoutFor);
            await db.Users
                .Apply(new UserByIdSpecification(user.Id)
                    .And(new UserWithMinAccessFailedCountSpecification(MaxFailed)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LockoutEnd, lockUntil)
                    .SetProperty(u => u.AccessFailedCount, 0), ct);

            return Error.Unauthorized("AUTH.INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, command.Password);

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        var roles = await db.UserRoles
            .Apply(new UserRolesByUserIdSpecification(user.Id))
            .Select(ur => ur.Role.Name)
            .ToListAsync(ct);

        var issued = tokens.IssueTokens(user, roles, Guid.NewGuid());
        db.RefreshTokens.Add(issued.RefreshToken);
        return new TokenResponse(
            issued.AccessToken,
            "Bearer",
            issued.AccessTokenExpiresInSeconds,
            issued.RawRefreshToken);
    }

    private void BurnPasswordVerify(string password) =>
        passwordHasher.VerifyHashedPassword(
            new User { Email = "x", NormalizedEmail = "X", PasswordHash = DummyHash },
            DummyHash,
            password);
}
```

**`LoginEndpoint.cs`** — JSON body binds the command → `MatchOk`; `Cache-Control: no-store` on token responses:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Login/LoginEndpoint.cs"
using Shared.Common.Errors.Http;

namespace MyApp.Features.Auth.Login;

public sealed class LoginEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/login", HandleAsync)
            .WithName("Login")
            .WithTags("Auth")
            .AllowAnonymous();
        // After Step 8: .RequireRateLimiting(ApiRateLimiting.AuthPolicy)
    }

    private static async Task<IResult> HandleAsync(
        LoginCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await mediator.Send(command, ct);
        return result.MatchOk(http);
    }
}
```

Business error codes:

| Case | Factory |
|------|---------|
| Wrong / unknown / banned / lockout (login) | `Error.Unauthorized("AUTH.INVALID_CREDENTIALS", ...)` — one message |
| Duplicate email (register) | `Error.Conflict("AUTH.EMAIL_TAKEN", ...)` |
| Bad refresh | `Error.Unauthorized("AUTH.INVALID_TOKEN", ...)` |
| Banned on refresh | `Error.Forbidden("AUTH.ACCOUNT_DISABLED", ...)` |
| Ban target missing | `Error.NotFound("AUTH.USER_NOT_FOUND", ...)` |
| Missing / failed bearer (Challenge) | `Error.Unauthorized("AUTH.UNAUTHORIZED", ...)` |
| Expired access JWT | `Error.Unauthorized("AUTH.TOKEN_EXPIRED", ...)` |
| Invalid access JWT | `Error.Unauthorized("AUTH.TOKEN_INVALID", ...)` |
| Policy deny (Forbidden) | `Error.Forbidden("AUTH.FORBIDDEN", ...)` |

### 5.3 RefreshToken (`Features/Auth/Refresh/`)

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Refresh/RefreshTokenCommand.cs"
using Shared.Application.Abstractions.Commands;
using MyApp.Features.Auth;

namespace MyApp.Features.Auth.Refresh;

public sealed record RefreshTokenCommand(string RefreshToken)
    : ICommand<TokenResponse>, IPersistOnFailure;
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Refresh/RefreshTokenCommandValidator.cs"
using FluentValidation;

namespace MyApp.Features.Auth.Refresh;

public sealed class RefreshTokenCommandValidator : AbstractValidator<RefreshTokenCommand>
{
    public RefreshTokenCommandValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty();
    }
}
```

**Why `IPersistOnFailure` here:** presenting an already-rotated refresh must revoke the **entire** `FamilyId` and still return `401`. That revoke is an `ExecuteUpdate` that must commit on the ErrorOr failure path.

**Reuse detection vs rotation**

```text
Client sends refresh R1
  → server rotates → R1.RevokedAt set, issues R2 (same FamilyId)
Attacker (or buggy client) replays R1
  → R1.RevokedAt != null → ExecuteUpdate revoke all FamilyId rows → 401
  → R2 is now dead too (family kill)
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Refresh/RefreshTokenCommandHandler.cs"
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.Refresh;

internal sealed class RefreshTokenCommandHandler(
    AppDbContext db,
    ITokenService tokens,
    TimeProvider clock)
    : IRequestHandler<RefreshTokenCommand, ErrorOr<TokenResponse>>
{
    public async ValueTask<ErrorOr<TokenResponse>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var hash = tokens.Hash(command.RefreshToken);
        var stored = await db.RefreshTokens
            .Apply(new RefreshTokenByHashSpecification(hash)
                .And(RefreshTokenWithUserRolesSpecification.Instance))
            .SingleOrDefaultAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;

        if (stored is null)
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");

        // Reuse: already-rotated token presented → kill the whole family.
        if (stored.RevokedAt is not null)
        {
            await db.RefreshTokens
                .Apply(new RefreshTokenByFamilyIdSpecification(stored.FamilyId)
                    .And(ActiveRefreshTokenSpecification.Instance))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");
        }

        if (stored.ExpiresAt <= now)
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");

        if (stored.User is null || stored.User.IsBanned)
            return Error.Forbidden("AUTH.ACCOUNT_DISABLED", "Account is locked or disabled.");

        var revoked = await db.RefreshTokens
            .Apply(new RefreshTokenByIdSpecification(stored.Id)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        if (revoked == 0)
        {
            await db.RefreshTokens
                .Apply(new RefreshTokenByFamilyIdSpecification(stored.FamilyId)
                    .And(ActiveRefreshTokenSpecification.Instance))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");
        }

        var roles = stored.User.UserRoles.Select(ur => ur.Role.Name);
        var issued = tokens.IssueTokens(stored.User, roles, stored.FamilyId);
        db.RefreshTokens.Add(issued.RefreshToken);
        return new TokenResponse(
            issued.AccessToken,
            "Bearer",
            issued.AccessTokenExpiresInSeconds,
            issued.RawRefreshToken);
    }
}
```

**`RefreshTokenEndpoint.cs`:**

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Refresh/RefreshTokenEndpoint.cs"
using Shared.Common.Errors.Http;

namespace MyApp.Features.Auth.Refresh;

public sealed class RefreshTokenEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/refresh", HandleAsync)
            .WithName("RefreshToken")
            .WithTags("Auth")
            .AllowAnonymous();
        // After Step 8: .RequireRateLimiting(ApiRateLimiting.AuthPolicy)
    }

    private static async Task<IResult> HandleAsync(
        RefreshTokenCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        http.Response.Headers.CacheControl = "no-store";
        var result = await mediator.Send(command, ct);
        return result.MatchOk(http);
    }
}
```

Expiry uses `UtcDateTime` (not the `DateTimeOffset` from `GetUtcNow()` directly) so comparison matches the stored `DateTime` column.

### 5.4 Logout / LogoutAll / Ban

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/Logout/LogoutCommand.cs"
using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.Logout;

public sealed record LogoutCommand(string RefreshToken) : ICommand;
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/LogoutAll/LogoutAllCommand.cs"
using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.LogoutAll;

public sealed record LogoutAllCommand(Guid UserId) : ICommand;
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/AdminUsers/BanUserCommand.cs"
using Shared.Application.Abstractions.Commands;

namespace MyApp.Features.Auth.AdminUsers;

public sealed record BanUserCommand(Guid UserId) : ICommand;
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/AdminUsers/BanUserCommandHandler.cs"
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Errors;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.AdminUsers;

internal sealed class BanUserCommandHandler(AppDbContext db, TimeProvider clock)
    : IRequestHandler<BanUserCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(BanUserCommand command, CancellationToken ct)
    {
        var user = await db.Users
            .Apply(new UserByIdSpecification(command.UserId)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return Error.NotFound("AUTH.USER_NOT_FOUND", "User was not found.");

        user.IsBanned = true;
        user.PermissionVersion++;

        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Apply(new RefreshTokenByUserIdSpecification(user.Id)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        return default(Success);
    }
}

// Also: UnbanUserCommand; Ban requires caller id ≠ target (self-ban guard).
// See AdminUsers/BanUserCommands.cs + BanUserCommandHandlers.cs.
```

`LogoutAllCommandHandler` is the same pattern without `IsBanned = true` (bump `PermissionVersion` + revoke via `ByUserId` `.And(ActiveRefreshToken)`). `LogoutCommandHandler` resolves family via `RefreshTokenByHashSpecification`, then `ByFamilyId` `.And(ActiveRefreshToken)` → `ExecuteUpdate`.

**Endpoints (same Products pattern):**

- Logout: body → `LogoutCommand` → `MatchNoContent(http)`; `.AllowAnonymous()` (+ rate limit in Step 8).
- Logout-all: `ClaimsPrincipal` → `LogoutAllCommand(user.GetUserId())` → `MatchNoContent(http)` (authenticated).
- Ban / unban: `.RequireAuthorization(Policies.AdminOnly)` after Step 7 → `MatchNoContent(http)`.

> **Handlers must not** raw-`.Include(...)` / `.Where(...)` for auth entity loads **or** `ExecuteUpdate` filters — use `Features/Auth/Specifications` + `Apply`.

### 5.5 Roles management (AdminOnly)

Catalog + membership. Carter routes use `.RequireAuthorization(Policies.AdminOnly)` once that policy is registered in **Step 7**:

| Method | Route | Purpose |
|--------|-------|---------|
| `GET` | `/auth/admin/roles` | List roles |
| `POST` | `/auth/admin/roles` | Create role (`Name` → unique `NormalizedName`) |
| `PUT` | `/auth/admin/roles/{roleId}` | Rename (seeded User/Admin → `AUTH.ROLE_PROTECTED`) |
| `DELETE` | `/auth/admin/roles/{roleId}` | Delete if no members; seeded protected; in-use → `AUTH.ROLE_IN_USE` |
| `GET` | `/auth/admin/users/{userId}/roles` | List roles for user |
| `POST` | `/auth/admin/users/{userId}/roles/{roleId}` | Assign — bumps `PermissionVersion`, revokes refresh |
| `DELETE` | `/auth/admin/users/{userId}/roles/{roleId}` | Remove — same invalidation |

Implementation: [`Features/Auth/AdminRoles/`](../../src/Services/MyApp.Api/Features/Auth/AdminRoles/) + membership handlers in [`AdminUsers/`](../../src/Services/MyApp.Api/Features/Auth/AdminUsers/). Specs: `RoleByIdSpecification`, `UserRoleByUserAndRoleSpecification`.

---

## Step 6: JWT validation (`ConfigureJwtBearer`)

### What this step is for

Step 4’s `TokenService` **issues** access JWTs. This step teaches how the API **accepts** them on every protected request.

When a client sends `Authorization: Bearer <access_token>`, ASP.NET Core’s JwtBearer middleware must:

1. Cryptographically validate the JWT (signature, issuer, audience, expiry, algorithm).
2. Build a `ClaimsPrincipal` (`sub`, `role`, `token_version`, …).
3. Optionally run **app-specific** checks after the crypto pass (banned user, `token_version` vs `PermissionVersion`).
4. On failure, return the same Problem Details shape as Products — not a bare `WWW-Authenticate` 401.

That configuration lives in one class: **`ConfigureJwtBearer`**. Registration is `AddJwtBearer()` + `ConfigureOptions<ConfigureJwtBearer>()`; the middleware then runs **before** Carter/Mediator on every request.

### Why a dedicated class? (`IConfigureNamedOptions<JwtBearerOptions>`)

You *could* dump options into `.AddJwtBearer(o => { … })` inside DI. Don’t — that buries validation rules, claim types, and event handlers in composition code.

Instead:

```csharp
public sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt)
    : IConfigureNamedOptions<JwtBearerOptions>
```

| Piece | Meaning |
|-------|---------|
| `IConfigureNamedOptions<JwtBearerOptions>` | ASP.NET Options pattern: when JwtBearer asks for its options, the container runs this class’s `Configure` and mutates `JwtBearerOptions`. |
| Named | JwtBearer registers a **named** options instance (`JwtBearerDefaults.AuthenticationScheme`). Implement `IConfigureNamedOptions` so you only configure that scheme (`if (name != …) return`). Plain `IConfigureOptions` alone can fire for the wrong name. |
| `IOptions<JwtOptions> jwt` | Your app settings (secret, issuer, audience, lifetimes) from Step 2 — the signing key and `ValidIssuer` / `ValidAudience` come from here. |
| `ConfigureOptions<ConfigureJwtBearer>()` | Registers the class so DI applies it automatically when `AddJwtBearer()` builds options. |

**Responsibilities of this class (only):**

- Fill `TokenValidationParameters` (how to trust a JWT).
- Map claim types (`sub` → name, `"role"` → roles) so role-based authorization (Step 7) can read them.
- Hook `JwtBearerEvents` for ban / `token_version` and Problem Details responses.

It does **not** issue tokens, talk to Carter, or return `ErrorOr`. Handlers still use `ErrorOr`; JwtBearer events cannot — they run outside Mediator. Use `Error` factories + [`ToProblem`](../../src/Shared/Common/Errors/Http/ErrorExtensions.cs) + `ExecuteAsync` on `HttpContext` instead.

### Validation options you must set

| Setting | Value | Why |
|---------|--------|-----|
| Scheme | HS256 symmetric key from `JwtOptions.Secret` — **no** `Authority` | We own the key; this is not OpenIddict/IdentityServer. |
| `TokenValidationParameters` | `ValidateIssuer` / `Audience` / `Lifetime` / `IssuerSigningKey` | Reject forged or mis-addressed tokens. |
| `ValidAlgorithms` | `{ SecurityAlgorithms.HmacSha256 }` only | Blocks `alg: none` and algorithm confusion. |
| `MapInboundClaims` | `false` | Keep JWT claim names as issued (`sub`, `role`) — don’t remap to long ClaimTypes URIs. |
| `NameClaimType` / `RoleClaimType` | `JwtRegisteredClaimNames.Sub` and `AuthClaims.Role` (`"role"`) | Must match what `TokenService` embeds or Step 7’s `AdminOnly` / `User.IsInRole` break. |
| `ClockSkew` | `TimeSpan.Zero` (or small) | Default 5 min is too loose for 10-min access tokens. |

### Problem Details helper (shared by all events)

Events should not each reinvent response writing. `JwtBearerProblem` sets `WWW-Authenticate` and writes Problem Details via `Error.ToProblem`:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/JwtBearerProblem.cs"
using Shared.Common.Errors;
using Shared.Common.Errors.Http;

namespace MyApp.Features.Auth;

internal static class JwtBearerProblem
{
    public const string ChallengeErrorItemKey = "auth.challengeError";

    public static async Task WriteAsync(HttpContext http, Error error)
    {
        if (http.Response.HasStarted) return;

        http.Response.Headers.WWWAuthenticate = error.Code switch
        {
            "AUTH.TOKEN_EXPIRED" => "Bearer error=\"invalid_token\", error_description=\"The access token has expired\"",
            "AUTH.TOKEN_INVALID" => "Bearer error=\"invalid_token\"",
            _ => "Bearer",
        };

        await error.ToProblem(http).ExecuteAsync(http);
    }
}
```

### Events — when each fires

Write the HTTP body in **`OnChallenge` / `OnForbidden`**. Call `HandleResponse()` so ASP.NET does not emit the default auth challenge. Guard with `HasStarted` so you never double-write.

| Event | When | What you do |
|-------|------|-------------|
| `OnTokenValidated` | Crypto OK; principal built | Load user; if banned or `token_version` mismatch → stash `Error` in `Items` + `context.Fail(...)` → pipeline continues to **Challenge** |
| `OnAuthenticationFailed` | Bad signature, expired, malformed, wrong alg | Prefer **not** writing here; let **OnChallenge** write once |
| `OnChallenge` | Auth required and missing/failed (incl. after `Fail`) | `401` Problem Details (`AUTH.UNAUTHORIZED` / `AUTH.TOKEN_EXPIRED` / `AUTH.TOKEN_INVALID` / `AUTH.ACCOUNT_DISABLED`) |
| `OnForbidden` | Authenticated but authorization policy failed (policies in Step 7) | `403` Problem Details (`AUTH.FORBIDDEN`) |

**Stash typed `Error` in `HttpContext.Items[ChallengeErrorItemKey]` from `OnTokenValidated`** so `OnChallenge` does not string-match failure messages.

### `ConfigureJwtBearer.cs` (full)

Put the options table + events into the `IConfigureNamedOptions` implementation:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Features/Auth/ConfigureJwtBearer.cs"
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApp.Config;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Errors;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth;

public sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        var o = jwt.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret));

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = o.Issuer,
            ValidateAudience = true,
            ValidAudience = o.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = AuthClaims.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnAuthenticationFailed = _ => Task.CompletedTask,
            OnChallenge = OnChallengeAsync,
            OnForbidden = OnForbiddenAsync,
        };
    }

    private static async Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        if (context.HttpContext.Response.HasStarted) return;

        var error = context.HttpContext.Items.TryGetValue(JwtBearerProblem.ChallengeErrorItemKey, out var boxed)
                    && boxed is Error stored
            ? stored
            : MapChallengeError(context);
        await JwtBearerProblem.WriteAsync(context.HttpContext, error);
    }

    private static async Task OnForbiddenAsync(ForbiddenContext context)
    {
        if (context.HttpContext.Response.HasStarted) return;
        await JwtBearerProblem.WriteAsync(
            context.HttpContext,
            Error.Forbidden("AUTH.FORBIDDEN", "You are not allowed to perform this action."));
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.UNAUTHORIZED", "Authentication is required.");
            context.Fail("Missing subject.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .Apply(new UserByIdSpecification(userId))
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user is null || user.IsBanned)
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.ACCOUNT_DISABLED", "Account is locked or disabled.");
            context.Fail("User banned or missing.");
            return;
        }

        var version = context.Principal?.FindFirst(AuthClaims.TokenVersion)?.Value;
        if (version is null || !int.TryParse(version, out var tv) || tv != user.PermissionVersion)
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.UNAUTHORIZED", "Authentication is required.");
            context.Fail("Token version mismatch.");
        }
    }

    private static Error MapChallengeError(JwtBearerChallengeContext context)
    {
        var ex = context.AuthenticateFailure
                 ?? context.HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Failure;

        if (ex is SecurityTokenExpiredException)
            return Error.Unauthorized("AUTH.TOKEN_EXPIRED", "The access token has expired.");

        if (ex is SecurityTokenException)
            return Error.Unauthorized("AUTH.TOKEN_INVALID", "The access token is invalid.");

        return Error.Unauthorized("AUTH.UNAUTHORIZED", "Authentication is required.");
    }
}
```

### Register JwtBearer (Presentation) — do this now

Authentication only in this step — fallback policy and `AdminOnly` come in **Step 7**.

`ConfigureJwtBearer` needs `JwtOptions` already registered (Step 2 classes). Bind them in Infrastructure if you have not yet:

```csharp
services.AddValidatedOptions<JwtOptions>(JwtOptions.Section);
```

Wire JwtBearer **inside `AddApi`**, not in `Program.cs`. `AddJwtBearer()` creates the scheme; `ConfigureOptions<ConfigureJwtBearer>()` attaches your class so options/events are applied:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Presentation/DependencyInjection.cs"
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MyApp.Features.Auth;

// Inside AddApi(...):
services.AddJwtAuthentication();

private static IServiceCollection AddJwtAuthentication(this IServiceCollection services)
{
    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(); // options come from ConfigureJwtBearer

    services.ConfigureOptions<ConfigureJwtBearer>();
    return services;
}
```

Pipeline (rate limiter from Step 8 goes **before** this):

```csharp
app.UseAuthentication();
```

`UseAuthorization()` is added in Step 7 with the policies.

**ErrorOr vs JwtBearer (authentication failures)**

| Path | Mechanism |
|------|-----------|
| Login / refresh / register / ban | `ErrorOr` → `Match*` / `ToProblem` |
| Missing/bad/expired JWT, ban/`token_version` on API call | JwtBearer events → `Error` + `ToProblem` + `ExecuteAsync` |

Same Problem Details fields (`status`, `title`, `detail`, `errorCode`, `errorType`, `traceId`) either way.

---

## Step 7: Authorization (fallback policy + `AdminOnly`)

### What this step is for

Step 6 answered *“who is this?”* (authentication). This step answers *“are they allowed?”* (authorization).

You register:

1. **Fallback policy** — every endpoint requires an authenticated user unless marked `.AllowAnonymous()` (secure by default).
2. **`AdminOnly`** — named policy requiring the `Admin` role claim (must match `RoleClaimType` / `TokenService` from Steps 4 and 6).

`OnForbidden` in `ConfigureJwtBearer` already writes `403` Problem Details when a policy fails; this step is what **defines** those policies.

### Register policies — do this now

```csharp title="MyApp/backend/src/Services/MyApp.Api/Presentation/DependencyInjection.cs"
using Microsoft.AspNetCore.Authorization;
using MyApp.Features.Auth;

// Inside AddApi(...):
services.AddJwtAuthentication();       // Step 6
services.AddAuthorizationPolicies();   // this step

private static IServiceCollection AddAuthorizationPolicies(this IServiceCollection services)
{
    services.AddAuthorization(options =>
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));
    });

    return services;
}
```

Pipeline — after `UseAuthentication`:

```csharp
app.UseAuthentication();   // Step 6
app.UseAuthorization();    // this step
```

With fallback on, mark public product GETs `.AllowAnonymous()` or accept that cutover. OpenAPI/Scalar/health also need `.AllowAnonymous()` (Step 9). Admin Carter routes from Step 5 use `.RequireAuthorization(Policies.AdminOnly)` now.

| Path | Mechanism |
|------|-----------|
| Authenticated but fails `AdminOnly` / other policy | `OnForbidden` → `AUTH.FORBIDDEN` Problem Details |

---

## Step 8: Rate limiting

```csharp title="MyApp/backend/src/Services/MyApp.Api/Presentation/ApiRateLimiting.cs"
namespace MyApp.Presentation;

public static class ApiRateLimiting
{
    public const string AuthPolicy = "auth";
}
```

```csharp title="MyApp/backend/src/Services/MyApp.Api/Presentation/RateLimitingExtensions.cs"
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using MyApp.Config;

namespace MyApp.Presentation;

public static class RateLimitingExtensions
{
    public static IServiceCollection AddApiRateLimiting(this IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = async (ctx, ct) =>
            {
                var seconds = 60;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                    seconds = Math.Max(1, (int)retryAfter.TotalSeconds);
                ctx.HttpContext.Response.Headers.RetryAfter = seconds.ToString();
                await ValueTask.CompletedTask;
            };

            // GlobalLimiter — every request (not a named policy you must remember to attach).
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var cfg = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetSlidingWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = cfg.GlobalPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                    });
            });

            options.AddPolicy(ApiRateLimiting.AuthPolicy, http =>
            {
                var cfg = http.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
                return RateLimitPartition.GetSlidingWindowLimiter(
                    http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = cfg.AuthPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6,
                    });
            });
        });
        return services;
    }
}
```

Call `AddApiRateLimiting()` from `AddApi` **now**, and add `app.UseRateLimiter()` in `UsePresentation` **before** `UseAuthentication` / `UseAuthorization` (Steps 6–7):

```csharp
// Inside AddApi(...):
services.AddApiRateLimiting();

// Inside UsePresentation(...):
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
```

Then add `.RequireRateLimiting(ApiRateLimiting.AuthPolicy)` on the Step 5 auth endpoints: `/auth/register`, `/auth/login`, `/auth/refresh`, `/auth/token`, and logout.

---

## Step 9: OpenAPI + Scalar

Bearer scheme + OAuth2 password flow (`tokenUrl: /auth/token`) for Scalar auto-inject. Prefer JSON `POST /auth/login` / `/auth/refresh` from clients; `/auth/token` is the form adapter only.

```csharp title="MyApp/backend/src/Services/MyApp.Api/Presentation/OpenApiAuthExtensions.cs"
// AddAuthSecuritySchemes: Bearer + oauth2 password → /auth/token
```

In Development `UsePresentation`: `.EnablePersistentAuthentication()` on Scalar; `.AllowAnonymous()` on OpenAPI/Scalar/health (fallback policy would otherwise block docs). Operation transformer requires Bearer on non-anonymous ops.

---

## Step 10: Wire into MyApp composition (Program stays thin)

### 10.1 Program.cs

Full file (DotNetEnv already shown in Step 2.3):

```csharp title="MyApp/backend/src/Services/MyApp.Api/Program.cs"
using DotNetEnv;
using MyApp.Application;
using MyApp.Infrastructure;
using MyApp.Presentation;

Env.TraversePath().Load(); // before CreateBuilder so Jwt__* bind into IConfiguration

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseInfrastructure()
    .UsePresentation();

app.Run();

public partial class Program;
```

**Do not** dump Jwt/Auth/rate-limit blocks into `Program.cs`.

### 10.2 Infrastructure — options + ITokenService + PasswordHasher

```csharp title="MyApp/backend/src/Services/MyApp.Api/Infrastructure/DependencyInjection.cs"
using Microsoft.AspNetCore.Identity;
using MyApp.Config;
using MyApp.Features.Auth;
using MyApp.Persistence.Extensions;

namespace MyApp.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDatabase(configuration);

        services.AddValidatedOptions<JwtOptions>(JwtOptions.Section);
        services.AddValidatedOptions<RateLimitOptions>(RateLimitOptions.Section);
        services.AddValidatedOptions<SeedOptions>(SeedOptions.Section);

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        return services;
    }
}
```

### 10.3 Presentation — compose `AddApi` (auth + authz + rate limit from Steps 6–8)

`AddJwtAuthentication`, `AddAuthorizationPolicies`, and `AddApiRateLimiting` were registered in Steps 6–8. Step 10 only assembles the full `AddApi` / `UsePresentation` surface:

```csharp title="MyApp/backend/src/Services/MyApp.Api/Presentation/DependencyInjection.cs"
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using MyApp.Features.Auth;
using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddCarter()
            .AddJwtAuthentication()        // Step 6
            .AddAuthorizationPolicies()    // Step 7
            .AddApiRateLimiting()          // Step 8
            .AddCorsPolicies(configuration)
            .AddOpenApiDocumentation()
            .AddApiVersioningSupport()
            .AddHealthChecks(configuration)
            .AddSharedExceptionHandling();

        return services;
    }

    // AddJwtAuthentication — see Step 6
    // AddAuthorizationPolicies — see Step 7
    // AddApiRateLimiting — see Step 8
    // AddCorsPolicies / AddOpenApiDocumentation / AddApiVersioningSupport / AddHealthChecks —
    // keep existing implementations; OpenAPI Bearer from Step 9.
}
```

### 10.4 Presentation pipeline

```csharp title="MyApp/backend/src/Services/MyApp.Api/Presentation/WebApplicationExtensions.cs"
using Scalar.AspNetCore;
using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;

public static class WebApplicationExtensions
{
    public static WebApplication UsePresentation(this WebApplication app)
    {
        app.UseSharedExceptionHandling();
        app.UseRateLimiter();       // Step 8
        app.UseAuthentication();    // Step 6
        app.UseAuthorization();     // Step 7

        app.MapCarter();
        app.MapHealthChecks("/health");

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi().AllowAnonymous();
            app.MapScalarApiReference().AllowAnonymous();
        }

        return app;
    }
}
```

Auth migrate + optional admin seed were wired in Step 3.4 (`UseInfrastructure`). Product `DataSeeder` stays Development-only and is unrelated to IAM.

---

## Step 11: Try it

```bash
cd MyApp/backend/src/Services/MyApp.Api
# Jwt__Secret set in .env
dotnet run
```

Scalar → `/scalar` → Authorize with Bearer after login.

```bash
curl -sk -X POST $API/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"ChangeMe-12345"}'

curl -sk -X POST $API/auth/refresh -H "Content-Type: application/json" \
  -d '{"refreshToken":"'$REFRESH'"}'

curl -sk $API/api/me -H "Authorization: Bearer $ACCESS"

curl -sk -X POST $API/auth/register -H "Content-Type: application/json" \
  -d '{"email":"bob@example.com","password":"A-long-password-123"}'
```

Ban bob as admin → bob’s access JWT gets **401 Problem Details** (`AUTH.ACCOUNT_DISABLED`) via `OnTokenValidated` → `OnChallenge`; refresh fails with Problem Details (`AUTH.*` codes). Missing Bearer on a protected route → `OnChallenge` → `AUTH.UNAUTHORIZED`. Admin-only as non-admin → `OnForbidden` → `AUTH.FORBIDDEN`.

Invalid login body (empty email) → **400 Validation** from `ValidationBehavior` / `LoginCommandValidator`, not from the handler.

---

## Edge cases

| Scenario | Handled by |
|---|---|
| Empty/malformed email or password on login | `LoginCommandValidator` → `ValidationBehavior` |
| Weak password on register | `RegisterUserCommandValidator` |
| Wrong password / unknown email | `LoginCommandHandler` → `Error.Unauthorized` |
| Banned / lockout (login) | `AUTH.INVALID_CREDENTIALS` (same as bad password — no enumeration) |
| Banned (refresh) | Handler → `Error.Forbidden("AUTH.ACCOUNT_DISABLED")` |
| Banned / `token_version` mismatch (bearer) | `OnTokenValidated` → `Fail` → `OnChallenge` → Problem Details |
| Expired / bad signature / wrong alg | `OnAuthenticationFailed` → `OnChallenge` → `AUTH.TOKEN_EXPIRED` / `AUTH.TOKEN_INVALID` |
| No Bearer on protected endpoint | `OnChallenge` → `AUTH.UNAUTHORIZED` |
| Authenticated but fails `AdminOnly` | `OnForbidden` → `AUTH.FORBIDDEN` |
| Forgot auth on endpoint | Fallback policy + `OnChallenge` |
| Docs blocked | `.AllowAnonymous()` on OpenAPI/Scalar |
| Refresh reuse | Atomic revoke + family kill |
| `alg: none` | `ValidAlgorithms` pinned |
| Weak JWT secret | `ValidateOnStart` |

---

## Before you go to production

1. Secrets from vault/orchestrator, not committed `.env`.  
2. EF migrations; consider Postgres.  
3. Housekeep expired refresh tokens.  
4. Distributed rate limits if multi-instance.  
5. Prefer BFF later so refresh never sits in JS storage.  
6. Multi-service → RS256/ES256 + JWKS.  
7. Audit bans, refresh-reuse, lockouts, 429s.

---

## Phase 2 — bridge to arch.test

| Capability | Direction |
|---|---|
| Fine-grained permissions | `Permission` + `RolePermission`; JWT `permissions` / `perm:*` |
| Cached revoke | `tokenver:{userId}` TTL + `POST /auth/revoke` |
| Social login | Google/GitHub + handoff code |
| Next.js BFF | Encrypted HttpOnly session; browser never sees JWT |
| Asymmetric JWT | ES256/RS256 + JWKS |
| Service accounts | Client-credentials + scopes |

Additive on **custom `User` + `PermissionVersion` + Products-style slices**.

---

> **Compile status:** samples target .NET 10 + MyApp’s Mediator / ErrorOr / Carter / FluentValidation stack. Auth security pipeline is implemented in the Api; acceptance coverage lives in `AuthSecurityTests`.

## Acceptance tests (required)

Integration tests in [`MyApp.Tests.Integration/AuthSecurityTests.cs`](../../tests/MyApp.Tests.Integration/AuthSecurityTests.cs) cover:

1. Non-admin → 403 on admin route; admin → 200 (RoleClaimType = `role`).
2. Login returns access + refresh; refresh happy path; replay rotated refresh → 401 and newest also dead (family revoke).
3. Five wrong passwords → lockout persists and correct password still fails (`IPersistOnFailure` + `ExecuteUpdate`).
4. Ban → still-valid access JWT → 401; unban → new login works; admin cannot ban self.
5. Logout revokes refresh family; LogoutAll bumps `PermissionVersion` (old access + refresh dead).
6. `POST /auth/token` password + refresh_token form adapter (Scalar).
7. Auth rate limit → 429 + `Retry-After`.
8. `/health` anonymous; protected → Problem Details + `WWW-Authenticate`.
9. Register works with roles seeded via `HasData`; duplicate email → conflict.
10. Failed non-auth command leaves DB unchanged; login failure persists lockout.

### TransactionBehavior notes

- Commands commit only when successful **or** when the command implements `IPersistOnFailure` (`LoginCommand`, `RefreshTokenCommand`).
- Unique violations map to `DB.CONFLICT` (409).
- Failure-path lockout / family revoke use `ExecuteUpdateAsync` so they are not lost on ErrorOr returns.
- Markers: `IErrorOr`, `ICommandMarker`, `IPersistOnFailure` in Shared; see [`TransactionBehavior.cs`](../../src/Services/MyApp.Api/Persistence/TransactionBehavior.cs).

**Quick usage checklist**

1. Ordinary writes → `ICommand` / `ICommand<T>` only; return `Error.*` to abort without persisting.
2. Intentional side effects on a 401/403 path → add `IPersistOnFailure` + prefer `ExecuteUpdateAsync`.
3. Mutate a loaded entity → `.And(ForUpdateSpecification<T>.Instance)` on the read.
4. Issue tokens → `var issued = tokens.IssueTokens(...); db.RefreshTokens.Add(issued.RefreshToken);` → map to `TokenResponse`
5. Roles in JWT use claim type `"role"` → JwtBearer `RoleClaimType = AuthClaims.Role`.

