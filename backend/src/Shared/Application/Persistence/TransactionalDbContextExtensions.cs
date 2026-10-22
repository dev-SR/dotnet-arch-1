using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Application.Persistence;

public static class TransactionalDbContextExtensions
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> and also as <see cref="DbContext"/>
    /// so <c>TransactionBehavior</c> can commit whatever contexts the request touched.
    /// </summary>
    public static IServiceCollection AddTransactionalDbContext<TContext>(
        this IServiceCollection services,
        Action<IServiceProvider, DbContextOptionsBuilder> optionsAction)
        where TContext : DbContext
    {
        services.AddDbContext<TContext>(optionsAction);
        services.AddScoped<DbContext>(sp => sp.GetRequiredService<TContext>());
        return services;
    }
}
