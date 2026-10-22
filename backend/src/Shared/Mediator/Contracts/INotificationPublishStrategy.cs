namespace Mediator;

/// <summary>How <see cref="IPublisher"/> invokes handlers for one notification.</summary>
public interface INotificationPublishStrategy
{
    ValueTask Publish<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification;
}
