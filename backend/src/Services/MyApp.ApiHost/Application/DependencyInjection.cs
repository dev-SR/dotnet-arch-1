using FluentValidation;
using MyApp.ApiHost;
using MyApp.ApiHost.Persistence;
using MyApp.Modular;
using Shared.Application.Behaviors;

namespace MyApp.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // Auto-discover MyApp.Modules.* (plus host) — no hard-coded typeof(...).Assembly list.
        var assemblies = ModuleAssemblyCatalog.GetHostAndModuleAssemblies(typeof(AssemblyMarker).Assembly);

        services.RegisterRequestHandlers(assemblies);
        services.AddHybridCache();

        services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
        services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
        services.AddPipelineBehavior(typeof(CachingBehavior<,>));
        services.AddPipelineBehavior(typeof(TransactionBehavior<,>));

        services.AddValidatorsFromAssemblies(assemblies, includeInternalTypes: true);

        return services;
    }
}
