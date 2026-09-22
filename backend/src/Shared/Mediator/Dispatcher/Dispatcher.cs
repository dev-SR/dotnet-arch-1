// Dispatcher/Dispatcher.cs
namespace Mediator;

internal sealed class Dispatcher(
    IServiceProvider provider,
    DispatcherRegistry registry) : IMediator
{
    public ValueTask<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!registry.RequestWrappers.TryGetValue(request.GetType(), out var wrapper))
        {
            throw new InvalidOperationException(
                $"No handler registered for request type '{request.GetType().FullName}'. " +
                $"Ensure the handler implements IRequestHandler<{request.GetType().Name}, TResponse> " +
                $"and that AddDispatcher scanned the correct assembly.");
        }
        // Reference-type cast - cheap, no boxing
        return ((RequestHandlerBase<TResponse>)wrapper).Handle(request, provider, cancellationToken);
    }
}
