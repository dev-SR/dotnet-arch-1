using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Modules.Orders.Persistence;

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

        services.AddDbContext<OrdersDbContext>(options => options.UseSqlite(cs));
        return services;
    }
}
