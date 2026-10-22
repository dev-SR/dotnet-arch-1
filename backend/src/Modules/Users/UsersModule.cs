using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyApp.Modular;
using MyApp.Modules.Users.Persistence;

namespace MyApp.Modules.Users;

public static class UsersModule
{
    /// <summary>
    /// Module bootstrap: migrate Users DB. Dev seeding runs via <see cref="DevelopmentSeederExtensions.UseDevelopmentSeeders"/>.
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

        return app;
    }
}
