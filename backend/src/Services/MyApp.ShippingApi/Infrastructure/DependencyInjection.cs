using Microsoft.EntityFrameworkCore;
using MyApp.ShippingApi.Persistence;
using Shared.Application.Persistence;

namespace MyApp.ShippingApi.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTransactionalDbContext<ShippingDbContext>((_, options) =>
            options.UseSqlite(configuration.GetConnectionString("Shipping")
                              ?? "Data Source=shipping.db"));

        return services;
    }
}
