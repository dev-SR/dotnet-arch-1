namespace Mediator;

internal sealed class ParallelNotificationPublishStrategy : INotificationPublishStrategy
{
    public async ValueTask Publish<TNotification>(
        IEnumerable<INotificationHandler<TNotification>> handlers,
        TNotification notification,
        CancellationToken cancellationToken)
        where TNotification : INotification
    {
        var tasks = handlers
            .Select(h => h.Handle(notification, cancellationToken).AsTask())
            .ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }
}
