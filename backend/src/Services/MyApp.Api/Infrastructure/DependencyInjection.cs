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
