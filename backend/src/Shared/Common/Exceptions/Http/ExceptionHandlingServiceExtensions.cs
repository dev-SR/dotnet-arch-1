using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Shared.Common.Exceptions.Http;

/// <summary>
/// Host registration for the shared exception → Problem Details channel.
/// Each ASP.NET host (gateway, future microservices) calls these; the handler lives here once.
/// </summary>
public static class ExceptionHandlingServiceExtensions
{
    public static IServiceCollection AddSharedExceptionHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails = ctx =>
                ctx.ProblemDetails.Extensions.TryAdd("traceId", ctx.HttpContext.TraceIdentifier);
        });
        services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
        return services;
    }

    public static WebApplication UseSharedExceptionHandling(this WebApplication app)
    {
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            SuppressDiagnosticsCallback = ctx => ExceptionMapping.IsExpected(ctx.Exception),
        });
        app.UseStatusCodePages();
        return app;
    }
}
