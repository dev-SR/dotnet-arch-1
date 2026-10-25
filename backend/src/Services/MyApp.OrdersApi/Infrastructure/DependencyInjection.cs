using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MyApp.OrdersApi.Persistence;
using Shared.Application.Persistence;

namespace MyApp.OrdersApi.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var cs = configuration.GetConnectionString("Orders") ?? "Data Source=orders.db";
        services.AddTransactionalDbContext<OrdersDbContext>((sp, options) =>
        {
            options.UseSqlite(cs);
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });

        return services;
    }
}
