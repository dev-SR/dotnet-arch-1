using System.Reflection;
using Mediator;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Shared.Common.Errors;

namespace Shared.Application.Behaviors;

public interface ICacheable
{
    string CacheKey { get; }
    TimeSpan? Expiration => null;
}

/// <summary>
/// Caches successful <c>ErrorOr&lt;T&gt;</c> results by caching the unwrapped <c>T</c>.
/// Failures are never cached (the handler still runs and its error is returned as-is).
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(
    HybridCache cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly Type? ErrorOrValueType =
        typeof(TResponse).IsGenericType
        && typeof(TResponse).GetGenericTypeDefinition() == typeof(ErrorOr<>)
            ? typeof(TResponse).GetGenericArguments()[0]
            : null;

    private static readonly MethodInfo? CacheErrorOrMethod = ErrorOrValueType is null
        ? null
        : typeof(CachingBehavior<TRequest, TResponse>)
            .GetMethod(nameof(CacheErrorOrAsync), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(ErrorOrValueType);

    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next(cancellationToken);

        if (CacheErrorOrMethod is not null)
        {
            var task = (Task<TResponse>)CacheErrorOrMethod.Invoke(
                this, [cacheable, next, cancellationToken])!;
            return await task;
        }

        return await CacheAsync(cacheable, next, cancellationToken);
    }

    private async Task<TResponse> CacheAsync(
        ICacheable cacheable,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
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

    // Invoked via reflection with TValue = the ErrorOr payload type of TResponse.
    private async Task<TResponse> CacheErrorOrAsync<TValue>(
        ICacheable cacheable,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var options = cacheable.Expiration is { } expiration
            ? new HybridCacheEntryOptions { Expiration = expiration }
            : null;

        var miss = false;
        TResponse? failure = default;
        try
        {
            var value = await cache.GetOrCreateAsync<TValue>(
                cacheable.CacheKey,
                async ct =>
                {
                    miss = true;
                    logger.LogInformation("Cache miss for {Key}", cacheable.CacheKey);
                    var produced = await next(ct);
                    if (produced is IErrorOr { IsError: true })
                    {
                        failure = produced;
                        throw new DoNotCacheException();
                    }
                    return ((ErrorOr<TValue>)(object)produced!).Value;
                },
                options,
                cancellationToken: cancellationToken);

            if (!miss)
                logger.LogInformation("Cache hit for {Key}", cacheable.CacheKey);

            return (TResponse)(object)(ErrorOr<TValue>)value;
        }
        catch (DoNotCacheException) when (failure is not null)
        {
            return failure;
        }
    }

    private sealed class DoNotCacheException : Exception;
}
