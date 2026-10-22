using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Customers;
using MyApp.Modules.Users.Persistence.Configurations;

namespace MyApp.Modules.Users.Persistence.Seeding;

public static class UsersDataSeeder
{
    public static async Task SeedAsync(
        UsersDbContext db,
        SeedOptions options,
        IPasswordHasher<User> passwordHasher,
        CancellationToken ct = default)
    {
        await SeedAdminAsync(db, options, passwordHasher, ct);
        await SeedUserAsync(db, options, passwordHasher, ct);
        await SeedCustomersAsync(db, ct);
    }

    private static async Task SeedAdminAsync(
        UsersDbContext db,
        SeedOptions options,
        IPasswordHasher<User> passwordHasher,
        CancellationToken ct)
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

    private static async Task SeedUserAsync(
        UsersDbContext db,
        SeedOptions options,
        IPasswordHasher<User> passwordHasher,
        CancellationToken ct)
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

    private static async Task SeedCustomersAsync(UsersDbContext db, CancellationToken ct)
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
