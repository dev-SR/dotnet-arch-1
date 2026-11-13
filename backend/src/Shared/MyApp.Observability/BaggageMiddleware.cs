using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MyApp.Observability;

/// <summary>
/// Copies user.id into Baggage for outbound calls and into a logger scope for this request.
/// Wolverine baggage propagation depends on the transport; carry business ids in the message
/// contract when a consumer needs them.
/// </summary>
public sealed class BaggageMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            await next(context);
            return;
        }

        var previous = Baggage.Current;
        Baggage.SetBaggage("user.id", userId);
        try
        {
            using (context.RequestServices
                       .GetRequiredService<ILoggerFactory>()
                       .CreateLogger("UserContext")
                       .BeginScope(new Dictionary<string, object> { ["user.id"] = userId }))
            {
                await next(context);
            }
        }
        finally
        {
            Baggage.Current = previous;
        }
    }
}
