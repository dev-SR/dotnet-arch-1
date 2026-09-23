// RequestHandlerWrapper.cs

using Microsoft.Extensions.DependencyInjection;

namespace Mediator;

internal abstract class RequestHandlerBase;

internal abstract class RequestHandlerBase<TResponse> : RequestHandlerBase
{
    public abstract ValueTask<TResponse> Handle(
        IRequest<TResponse> request,
        IServiceProvider provider,
        CancellationToken cancellationToken);
}

internal sealed class RequestHandlerWrapper<TRequest, TResponse> : RequestHandlerBase<TResponse>
    where TRequest : IRequest<TResponse>
{
    public override ValueTask<TResponse> Handle(
        IRequest<TResponse> request,
        IServiceProvider provider,
        CancellationToken cancellationToken)
    {
        var typed = (TRequest)request;
        // Resolve handler from DI and invoke directly
        var handler = provider.GetRequiredService<IRequestHandler<TRequest, TResponse>>();
        return handler.Handle(typed, cancellationToken);
    }
}
