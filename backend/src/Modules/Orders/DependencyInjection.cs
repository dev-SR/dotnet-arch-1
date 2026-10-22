using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Modules.Orders.Persistence;
using Shared.Application.Persistence;

namespace MyApp.Modules.Orders;

public static class DependencyInjection
{
    public static IServiceCollection AddOrdersModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var cs = configuration.GetConnectionString("Orders")
                 ?? "Data Source=orders.db";

        services.AddTransactionalDbContext<OrdersDbContext>((sp, options) =>
        {
            options.UseSqlite(cs);
            options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
        });

        return services;
    }
}
