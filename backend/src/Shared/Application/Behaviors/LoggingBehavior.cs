// Behaviors/LoggingBehavior.cs

using System.Diagnostics;
using Mediator;
using Microsoft.Extensions.Logging;

namespace Shared.Application.Behaviors;

public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        logger.LogInformation("→ {RequestName} started", requestName);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await next(cancellationToken);
            stopwatch.Stop();
            logger.LogInformation(
                "← {RequestName} completed in {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            logger.LogError(
                ex,
                "✕ {RequestName} failed after {ElapsedMs}ms",
                requestName,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
