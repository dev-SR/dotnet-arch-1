using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyApp.Modular;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Customers;
using MyApp.Modules.Users.Persistence.Configurations;

namespace MyApp.Modules.Users.Persistence.Seeding;

public sealed class UsersDataSeeder(
    UsersDbContext db,
    IOptions<SeedOptions> seedOptions,
    IPasswordHasher<User> passwordHasher) : IDataSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
    {
        var options = seedOptions.Value;
        await SeedAdminAsync(options, ct);
        await SeedUserAsync(options, ct);
        await SeedCustomersAsync(ct);
    }

    private async Task SeedAdminAsync(SeedOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.AdminEmail) ||
            string.IsNullOrWhiteSpace(options.AdminPassword))
            return;

        var email = options.AdminEmail.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
            return;

        var user = new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, options.AdminPassword);
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleConfiguration.AdminRoleId });
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleConfiguration.UserRoleId });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedUserAsync(SeedOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.UserEmail) ||
            string.IsNullOrWhiteSpace(options.UserPassword))
            return;

        var email = options.UserEmail.Trim();
        var normalizedEmail = email.ToUpperInvariant();
        if (await db.Users.AnyAsync(u => u.NormalizedEmail == normalizedEmail, ct))
            return;

        var user = new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty,
        };
        user.PasswordHash = passwordHasher.HashPassword(user, options.UserPassword);
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = RoleConfiguration.UserRoleId });
        await db.SaveChangesAsync(ct);
    }

    private async Task SeedCustomersAsync(CancellationToken ct)
    {
        if (await db.Customers.AnyAsync(ct))
            return;

        // Small fixed set — Catalog/Orders reference CustomerId by GUID only (no cross-DB FK).
        db.Customers.AddRange(
            new Customer { Id = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), Name = "Ada Customer", Email = "ada@example.com" },
            new Customer { Id = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), Name = "Bob Customer", Email = "bob@example.com" });
        await db.SaveChangesAsync(ct);
    }
}
