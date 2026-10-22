using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Mediator;

internal sealed class Publisher(
    IServiceProvider provider,
    INotificationPublishStrategy publishStrategy) : IPublisher
{
    private static readonly ConcurrentDictionary<Type, Func<IServiceProvider, INotificationPublishStrategy, INotification, CancellationToken, ValueTask>>
        PublishDelegates = new();

    public ValueTask Publish(
        INotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);

        var publish = PublishDelegates.GetOrAdd(notification.GetType(), static type =>
        {
            var method = typeof(Publisher)
                .GetMethod(nameof(PublishCore), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(type);
            return (Func<IServiceProvider, INotificationPublishStrategy, INotification, CancellationToken, ValueTask>)
                Delegate.CreateDelegate(
                    typeof(Func<IServiceProvider, INotificationPublishStrategy, INotification, CancellationToken, ValueTask>),
                    method);
        });

        return publish(provider, publishStrategy, notification, cancellationToken);
    }

    private static ValueTask PublishCore<TNotification>(
        IServiceProvider provider,
        INotificationPublishStrategy strategy,
        INotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var handlers = provider.GetServices<INotificationHandler<TNotification>>();
        return strategy.Publish(handlers, (TNotification)notification, cancellationToken);
    }
}
