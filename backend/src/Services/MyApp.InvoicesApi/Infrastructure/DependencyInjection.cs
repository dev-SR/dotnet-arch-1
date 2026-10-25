using Microsoft.EntityFrameworkCore;
using Shared.Application.Persistence;

namespace MyApp.InvoicesApi.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddTransactionalDbContext<InvoicesDbContext>((_, options) =>
            options.UseSqlite(configuration.GetConnectionString("Invoices")
                              ?? "Data Source=invoices.db"));

        return services;
    }
}
