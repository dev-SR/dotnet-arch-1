using MyApp.Config;

namespace MyApp.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddValidatedOptions<RateLimitOptions>(RateLimitOptions.Section);
        return services;
    }
}
