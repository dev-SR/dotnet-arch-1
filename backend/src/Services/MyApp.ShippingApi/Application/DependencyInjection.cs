using MyApp.Modular;
using Shared.Application.Behaviors;

namespace MyApp.ShippingApi.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(AssemblyMarker).Assembly;

        services.RegisterRequestHandlers(assembly);
        services.AddPipelineBehavior(typeof(ErrorTracingBehavior<,>));

        return services;
    }
}
