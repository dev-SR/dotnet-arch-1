using System.Collections.Concurrent;
using System.Reflection;

namespace Shared.Common.Errors;

/// <summary>
/// Builds <see cref="ErrorOr{T}"/> failure values when the concrete T is only known as
/// <c>TResponse</c> (open-generic pipeline behaviors). Prefer returning <c>Error</c> /
/// <c>List&lt;Error&gt;</c> directly from handlers where T is known.
/// </summary>
public static class ErrorOrFactory
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> Cache = new();

    public static TResponse Failure<TResponse>(List<Error> errors)
    {
        var method = Cache.GetOrAdd(typeof(TResponse), static responseType =>
        {
            if (!responseType.IsGenericType
                || responseType.GetGenericTypeDefinition() != typeof(ErrorOr<>))
            {
                throw new InvalidOperationException(
                    $"{responseType.Name} is not ErrorOr<>. Handlers should return ErrorOr<T>.");
            }

            return typeof(ErrorOrFactory)
                .GetMethod(nameof(Create), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(responseType.GetGenericArguments()[0]);
        });

        return (TResponse)method.Invoke(null, [errors])!;
    }

    private static ErrorOr<T> Create<T>(List<Error> errors) => ErrorOr<T>.From(errors);
}
