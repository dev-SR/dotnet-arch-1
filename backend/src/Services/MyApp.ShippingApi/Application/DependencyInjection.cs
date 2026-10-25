using MyApp.Modular;

namespace MyApp.ShippingApi.Application;

public static class ApplicationServiceCollectionExtensions
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(AssemblyMarker).Assembly;
        services.RegisterRequestHandlers(assembly);
        return services;
    }
}
