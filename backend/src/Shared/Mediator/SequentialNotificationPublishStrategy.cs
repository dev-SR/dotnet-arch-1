namespace Mediator;

internal sealed class SequentialNotificationPublishStrategy : INotificationPublishStrategy
{
    public async ValueTask Publish<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        foreach (var handler in handlers)
            await handler.Handle(notification, cancellationToken).ConfigureAwait(false);
    }
}
