using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Auth.Authorization;
using MyApp.Modules.Users.Persistence;

namespace MyApp.Modules.Users;

public static class DependencyInjection
{
    public static IServiceCollection AddUsersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var cs = configuration.GetConnectionString("Users")
                 ?? "Data Source=users.db";

        services.AddDbContext<UsersDbContext>(options => options.UseSqlite(cs));

        services.AddOptions<SeedOptions>()
            .BindConfiguration(SeedOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<ITokenService, TokenService>();
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
        services.ConfigureOptions<ConfigureJwtBearer>();

        services.Configure<AuthorizationOptions>(options =>
        {
            options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));
            options.AddPolicy(Policies.WorkingHours, p =>
                p.Requirements.Add(new WorkingHoursRequirement(9, 18)));
            options.AddPolicy(Policies.EnhancedSecurity, p =>
                p.Requirements.Add(new EnhancedSecurityRequirement()));
        });
        services.AddSingleton<IAuthorizationHandler, WorkingHoursHandler>();
        services.AddSingleton<IAuthorizationHandler, EnhancedSecurityHandler>();

        return services;
    }
}
