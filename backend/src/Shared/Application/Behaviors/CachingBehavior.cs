// Behaviors/ICacheable.cs

using Mediator;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;

namespace Shared.Application.Behaviors;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan? Expiration => null;
}
// Behaviors/CachingBehavior.cs
public sealed class CachingBehavior<TRequest, TResponse>(
    HybridCache cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next(cancellationToken);

        var options = cacheable.Expiration is { } expiration
            ? new HybridCacheEntryOptions { Expiration = expiration }
            : null;

        var miss = false;

        var result = await cache.GetOrCreateAsync(
            cacheable.CacheKey,
            async ct =>
            {
                miss = true;
                logger.LogInformation("Cache miss for {Key}", cacheable.CacheKey);
                return await next(ct);
            },
            options,
            cancellationToken: cancellationToken);

        if (!miss)
            logger.LogInformation("Cache hit for {Key}", cacheable.CacheKey);

        return result;
    }
}
