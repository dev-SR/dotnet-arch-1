using Microsoft.Extensions.Logging;

namespace MyApp.Modules.Catalog.Products.ProductCreated;

public sealed class LogProductCreatedHandler(ILogger<LogProductCreatedHandler> logger)
    : INotificationHandler<ProductCreatedDomainEvent>
{
    public ValueTask Handle(
        ProductCreatedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Product created: {ProductId} - {Name}",
            notification.ProductId,
            notification.Name);
        return ValueTask.CompletedTask;
    }
}
