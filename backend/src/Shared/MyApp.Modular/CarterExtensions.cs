using System.Reflection;
using Carter;
using Microsoft.Extensions.DependencyInjection;

namespace MyApp.Modular;

public static class CarterExtensions
{
    public static IServiceCollection AddCarterWithAssemblies(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        services.AddCarter(configurator: config =>
        {
            foreach (var assembly in assemblies)
            {
                var modules = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && t.IsAssignableTo(typeof(ICarterModule)))
                    .ToArray();
                if (modules.Length > 0)
                    config.WithModules(modules);
            }
        });
        return services;
    }
}
