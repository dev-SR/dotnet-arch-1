using Microsoft.Extensions.DependencyInjection;

namespace Mediator;

public static class MediatorPipelineBehaviorExtension
{
    public static IServiceCollection AddPipelineBehavior(
        this IServiceCollection services,
        Type openGenericBehaviorType)
    {
        services.AddScoped(typeof(IPipelineBehavior<,>), openGenericBehaviorType);
        return services;
    }
}
