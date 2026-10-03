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


        if (!app.Environment.IsDevelopment()) return app;
        var seedOptions = sp
            .GetRequiredService<IOptions<SeedOptions>>()
            .Value;

        var passwordHasher = sp
            .GetRequiredService<IPasswordHasher<User>>();

        DataSeeder
            .SeedAsync(db, seedOptions, passwordHasher)
            .GetAwaiter()
            .GetResult();

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
