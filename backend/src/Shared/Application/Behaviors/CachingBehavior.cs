using System.Collections.Concurrent;
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

public sealed class CachingBehavior<TRequest, TResponse>(
    HybridCache cache,
    ILogger<CachingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly bool ResponseIsErrorOr =
        typeof(TResponse).IsGenericType
        && typeof(TResponse).GetGenericTypeDefinition() == typeof(ErrorOr<>);

    private static readonly ConcurrentDictionary<Type, Func<object, bool>> ErrorCheck = new();

    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not ICacheable cacheable)
            return await next(cancellationToken);

        // HybridCache cannot round-trip ErrorOr<T> (private fields). Skip until a
        // value-type-aware cache wrapper exists; still run the handler.
        if (ResponseIsErrorOr)
            return await next(cancellationToken);

        var options = cacheable.Expiration is { } expiration
            ? new HybridCacheEntryOptions { Expiration = expiration }
            : null;

        try
        {
            var miss = false;
            var result = await cache.GetOrCreateAsync(
                cacheable.CacheKey,
                async ct =>
                {
                    miss = true;
                    logger.LogInformation("Cache miss for {Key}", cacheable.CacheKey);
                    var produced = await next(ct);
                    if (IsFailedErrorOr(produced))
                        throw new DoNotCacheException(produced!);
                    return produced;
                },
                options,
                cancellationToken: cancellationToken);

            if (!miss)
                logger.LogInformation("Cache hit for {Key}", cacheable.CacheKey);

            return result;
        }
        catch (DoNotCacheException ex)
        {
            return ex.GetResult<TResponse>();
        }
    }

    private static bool IsFailedErrorOr(object? response)
    {
        if (response is null) return false;
        var checker = ErrorCheck.GetOrAdd(response.GetType(), static type =>
        {
            if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ErrorOr<>))
                return static _ => false;

            var prop = type.GetProperty(nameof(ErrorOr<int>.IsError))!;
            return obj => (bool)prop.GetValue(obj)!;
        });
        return checker(response);
    }

    private sealed class DoNotCacheException(object result) : Exception
    {
        public T GetResult<T>() => (T)result;
    }
}
