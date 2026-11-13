using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MyApp.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    /// <summary>
    /// One call per host. Add it to the Add* chain in Program.cs.
    /// </summary>
    public static IServiceCollection AddMyAppObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                       ?? "http://localhost:4317";
        var environment = configuration["OTEL_DEPLOYMENT_ENVIRONMENT"]
                          ?? configuration["ASPNETCORE_ENVIRONMENT"]?.ToLowerInvariant()
                          ?? "dev";

        // The default propagator is W3C TraceContext + Baggage.
        // Override with OTEL_PROPAGATORS when a non-default propagator is required.

        var otel = services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: typeof(ObservabilityServiceCollectionExtensions)
                        .Assembly.GetName().Version?.ToString() ?? "0.0.0")
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = environment,
                    ["host.name"] = Environment.MachineName,
                }));

        otel.WithTracing(tracing => tracing
            .AddSource("Wolverine")
            .AddSource("MyApp.Catalog")
            .AddSource("MyApp.Orders")
            .AddSource("MyApp.Search")
            .AddSource("MyApp.Invoices")
            .AddSource("MyApp.Shipping")
            .AddAspNetCoreInstrumentation(options =>
            {
                options.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health")
                    && !context.Request.Path.StartsWithSegments("/openapi");
            })
            .AddHttpClientInstrumentation()
            // The EF Core 1.13-beta instrumentation does not expose
            // SetDbStatementForText. Verify which db.* attributes it emits before
            // treating the production configuration as PII-safe.
            .AddEntityFrameworkCoreInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(endpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            }));

        otel.WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Wolverine*")
            .AddMeter("MyApp.Catalog")
            .AddMeter("MyApp.Orders")
            .AddMeter("MyApp.Search")
            .AddMeter("MyApp.Invoices")
            .AddMeter("MyApp.Shipping")
            .AddOtlpExporter((exporter, reader) =>
            {
                exporter.Endpoint = new Uri(endpoint);
                exporter.Protocol = OtlpExportProtocol.Grpc;
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15_000;
            }));

        services.AddLogging(logging => logging.AddOpenTelemetry(options =>
        {
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = new Uri(endpoint);
                exporter.Protocol = OtlpExportProtocol.Grpc;
            });
        }));

        return services;
    }
}
