using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Mediator;

public static class MediatorPublishExtensions
{
    /// <summary>Default: handlers run one after another.</summary>
    public static IServiceCollection UseSequentialNotificationPublish(this IServiceCollection services)
    {
        services.RemoveAll<INotificationPublishStrategy>();
        services.AddSingleton<INotificationPublishStrategy, SequentialNotificationPublishStrategy>();
        return services;
    }

    /// <summary>Handlers for the same notification run concurrently via Task.WhenAll.</summary>
    public static IServiceCollection UseParallelNotificationPublish(this IServiceCollection services)
    {
        services.RemoveAll<INotificationPublishStrategy>();
        services.AddSingleton<INotificationPublishStrategy, ParallelNotificationPublishStrategy>();
        return services;
    }
}
