using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyApp.Modular;
using MyApp.Modules.Orders.Persistence;

namespace MyApp.Modules.Orders;

public static class OrdersModule
{
    public static WebApplication UseOrdersModule(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrdersDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("OrdersModule");

        try
        {
            DbContextSchema.Apply(db);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Orders database migrate failed");
            throw;
        }

        return app;
    }
}
