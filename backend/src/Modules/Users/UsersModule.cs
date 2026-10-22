using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyApp.Modular;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Persistence;
using MyApp.Modules.Users.Persistence.Seeding;

namespace MyApp.Modules.Users;

public static class UsersModule
{
    /// <summary>
    /// Module bootstrap: migrate Users DB and (in Development) seed IAM/customers.
    /// </summary>
    public static WebApplication UseUsersModule(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<UsersDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger("UsersModule");

        try
        {
            DbContextSchema.Apply(db);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Users database migrate failed");
            throw;
        }

        // Integration tests seed explicitly; Catalog Bogus seed is Dev-only.
        if (!app.Environment.IsDevelopment())
            return app;

        var seed = sp.GetRequiredService<IOptions<SeedOptions>>().Value;
        var hasher = sp.GetRequiredService<IPasswordHasher<User>>();
        UsersDataSeeder.SeedAsync(db, seed, hasher).GetAwaiter().GetResult();

        return app;
    }
}
