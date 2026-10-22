namespace Mediator;

public interface IPublisher
{
    ValueTask Publish(INotification notification, CancellationToken cancellationToken = default);
}
