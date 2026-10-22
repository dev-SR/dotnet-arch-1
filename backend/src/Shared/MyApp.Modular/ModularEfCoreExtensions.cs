using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using MyApp.Modular.Persistence;

namespace MyApp.Modular;

public static class ModularEfCoreExtensions
{
    /// <summary>
    /// Registers audit + domain-event save interceptors and <see cref="IUserContext"/>.
    /// Call once on the host; each module DbContext adds <c>sp.GetServices&lt;ISaveChangesInterceptor&gt;()</c>.
    /// </summary>
    public static IServiceCollection AddModularEfCore(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUserContext, HttpUserContext>();
        services.AddScoped<ISaveChangesInterceptor, AuditableEntityInterceptor>();
        services.AddScoped<ISaveChangesInterceptor, DispatchDomainEventsInterceptor>();
        return services;
    }
}
