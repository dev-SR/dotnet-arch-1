# OpenTelemetry Dev Quickstart

> **What this is:** a minimal, runnable observability setup for local development — one collector, the Grafana stack, and all five .NET hosts (Gateway, ApiHost, OrdersApi, InvoicesApi, ShippingApi).
>
> **What this is not:** a production design. No HA, no TLS, no sampling policy, no retention policy, no SLOs. Those are a separate concern.
>
> **Verified:** the Docker stack in this guide was started, all five hosts were run, and both traces described below were observed in Tempo. Logs arrived in Loki with `trace_id`, metrics arrived in Prometheus, and the custom Catalog counter appeared. The code in §3–§7 is in the repo.

---

## 0. Topology (what actually happens)

Ports are the real ones from `launchSettings.json`:

| Host | Port | Role |
| --- | --- | --- |
| `MyApp.Gateway` | 5100 | YARP proxy + Scalar |
| `MyApp.ApiHost` | 5101 | Modular monolith (Users, Catalog, Search) |
| `MyApp.InvoicesApi` | 5102 | Extracted service |
| `MyApp.OrdersApi` | 5103 | Extracted service |
| `MyApp.ShippingApi` | 5104 | Extracted service |

Integration-event fan-out is:

| Event | Published by | Consumed by |
| --- | --- | --- |
| `ProductCreatedIntegrationEvent` | Catalog (ApiHost) | Search (in-process), OrdersApi |
| `ProductUpdatedIntegrationEvent` | Catalog (ApiHost) | Search (in-process), OrdersApi |
| `OrderPlacedIntegrationEvent` | OrdersApi | InvoicesApi, ShippingApi |

So:

- `POST /products` produces **one trace** that includes ApiHost (with the in-process Search handler) and OrdersApi.
- Placing an order produces **one trace** that includes OrdersApi + InvoicesApi + ShippingApi.

Wolverine creates producer/consumer activities with a parent/child relationship in the same trace. It does not create separate linked traces for RabbitMQ fan-out.

```
Browser / curl
   │
   ▼
Gateway :5100 ──► ApiHost :5101 ──► RabbitMQ ──► Search (in-process module)
                                             ├─► OrdersApi :5103
                                             └─► (on order placed) Invoices :5102, Shipping :5104
   │
   └── all hosts ──OTLP/gRPC :4317──► otel-collector
                                          ├─ traces  ──► Tempo   :3200
                                          ├─ logs    ──► Loki    :3100 (OTLP HTTP)
                                          └─ metrics ──► :8889 ◄── Prometheus :9090
                                                                     │
                                                              Grafana :3000
```

---

## 1. Bring up the backends

Create `observability-dev/` next to `backend/`. Six files, six containers.

### 1.1 `docker-compose.yml`

```yaml
services:
  otel-collector:
    image: otel/opentelemetry-collector-contrib:0.110.0
    container_name: otel-collector
    command: ["--config=/etc/otelcol/config.yaml"]
    volumes:
      - ./otel-collector.yaml:/etc/otelcol/config.yaml:ro
    ports:
      - "4317:4317"    # OTLP gRPC
      - "4318:4318"    # OTLP HTTP
      - "8889:8889"    # Prometheus scrape
    depends_on:
      - tempo
      - loki
    restart: unless-stopped

  prometheus:
    image: prom/prometheus:v2.55.1
    container_name: prometheus
    ports: ["9090:9090"]
    command:
      - --config.file=/etc/prometheus/prometheus.yml
      - --enable-feature=exemplar-storage
    volumes:
      - ./prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - prom-data:/prometheus
    restart: unless-stopped

  loki:
    image: grafana/loki:3.2.1
    container_name: loki
    command: -config.file=/etc/loki/loki.yml
    ports: ["3100:3100"]
    volumes:
      - ./loki.yml:/etc/loki/loki.yml:ro
      - loki-data:/loki
    restart: unless-stopped

  tempo:
    image: grafana/tempo:2.6.0
    container_name: tempo
    command: -config.file=/etc/tempo/tempo.yml
    ports: ["3200:3200"]
    volumes:
      - ./tempo.yml:/etc/tempo/tempo.yml:ro
      - tempo-data:/var/tempo
    restart: unless-stopped

  grafana:
    image: grafana/grafana:11.3.0
    container_name: grafana
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: admin
      GF_AUTH_ANONYMOUS_ENABLED: "false"
    ports: ["3000:3000"]
    volumes:
      - ./grafana-datasources.yml:/etc/grafana/provisioning/datasources/datasources.yml:ro
      - grafana-data:/var/lib/grafana
    depends_on:
      - prometheus
      - loki
      - tempo
    restart: unless-stopped

  alertmanager:
    image: prom/alertmanager:v0.27.0
    container_name: alertmanager
    ports: ["9093:9093"]
    volumes:
      - ./alertmanager.yml:/etc/alertmanager/alertmanager.yml:ro
    restart: unless-stopped

volumes:
  prom-data:
  loki-data:
  tempo-data:
  grafana-data:
```

### 1.2 `otel-collector.yaml`

```yaml
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
      http: { endpoint: 0.0.0.0:4318 }

processors:
  memory_limiter:
    check_interval: 1s
    limit_mib: 256
  batch:
    timeout: 1s
    send_batch_size: 512

exporters:
  # Metrics: expose for Prometheus to scrape
  prometheus:
    endpoint: 0.0.0.0:8889
    resource_to_telemetry_conversion:
      enabled: true

  # Traces
  otlp/tempo:
    endpoint: tempo:4317
    tls: { insecure: true }

  # Logs: Loki's OTLP HTTP endpoint (not gRPC 4317)
  otlphttp/loki:
    endpoint: http://loki:3100/otlp

  debug:
    verbosity: basic

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlp/tempo, debug]
    metrics:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [prometheus]
    logs:
      receivers: [otlp]
      processors: [memory_limiter, batch]
      exporters: [otlphttp/loki]
```

### 1.3 `prometheus.yml`

```yaml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: otel-collector
    static_configs:
      - targets: ['otel-collector:8889']
```

### 1.4 `loki.yml`

```yaml
auth_enabled: false

server:
  http_listen_port: 3100

common:
  path_prefix: /loki
  storage:
    filesystem:
      chunks_directory: /loki/chunks
      rules_directory: /loki/rules
  replication_factor: 1
  ring:
    kvstore: { store: inmemory }

distributor:
  otlp_config:
    default_resource_attributes_as_index_labels:
      - service.name
      - deployment.environment

limits_config:
  allow_structured_metadata: true
  retention_period: 168h

schema_config:
  configs:
    - from: 2024-01-01
      store: tsdb
      object_store: filesystem
      schema: v13
      index: { prefix: index_, period: 24h }

storage_config:
  filesystem: { directory: /loki/storage }
  tsdb_shipper:
    active_index_directory: /loki/tsdb-index
    cache_location: /loki/tsdb-cache
```

### 1.5 `tempo.yml`

```yaml
server:
  http_listen_port: 3200

distributor:
  receivers:
    otlp:
      protocols:
        grpc: { endpoint: 0.0.0.0:4317 }

storage:
  trace:
    backend: local
    local:
      path: /var/tempo/blocks
    wal:
      path: /var/tempo/wal

compactor:
  compaction:
    block_retention: 48h
```

### 1.6 `grafana-datasources.yml`

```yaml
apiVersion: 1

datasources:
  - name: Prometheus
    type: prometheus
    uid: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true

  - name: Loki
    type: loki
    uid: loki
    access: proxy
    url: http://loki:3100
    jsonData:
      derivedFields:
        # OTel logs carry trace_id as structured metadata, not inside the message.
        - name: TraceID
          matcherType: label
          matcherRegex: trace_id
          datasourceUid: tempo
          url: '$${__value.raw}'
          urlDisplayLabel: 'View trace'

  - name: Tempo
    type: tempo
    uid: tempo
    access: proxy
    url: http://tempo:3200
    jsonData:
      tracesToLogsV2:
        datasourceUid: loki
        filterByTraceID: true
        tags: ['service.name']
      nodeGraph:
        enabled: true

  - name: Alertmanager
    type: alertmanager
    uid: alertmanager
    access: proxy
    url: http://alertmanager:9093
    jsonData:
      implementation: prometheus
```

### 1.7 `alertmanager.yml`

```yaml
route:
  receiver: dev-null

receivers:
  - name: dev-null
```

### 1.8 Start it

```bash
cd observability-dev
docker compose up -d
docker compose ps
```

All six should be running. If the collector restarts, run `docker compose logs otel-collector --tail 30`.

---

## 2. Add the packages

The kit uses central package management. Add to `Directory.Packages.props`:

```xml
<!-- OpenTelemetry (dev) -->
<PackageVersion Include="OpenTelemetry" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
<PackageVersion Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.13.0-beta.1" />
```

Do **not** add `WolverineFx.OpenTelemetry` — that package does not exist. Wolverine's OpenTelemetry support is built in (see §5).

---

## 3. Create one shared observability project

Telemetry is host-level, but the same wiring is needed by five hosts. Create a shared project referenced only by hosts:

```text
src/Shared/MyApp.Observability/
  MyApp.Observability.csproj
  ObservabilityServiceCollectionExtensions.cs
  BaggageMiddleware.cs
```

`MyApp.Observability.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>MyApp.Observability</RootNamespace>
    <AssemblyName>MyApp.Observability</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
    <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
    <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
    <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
    <PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" />
  </ItemGroup>
</Project>
```

`ObservabilityServiceCollectionExtensions.cs`:

```csharp
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
    /// One call per host. Call it from Program.cs in the Add* chain.
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
        // Override with OTEL_PROPAGATORS if a non-default propagator is required.

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
            // Wolverine's built-in ActivitySource (no extra package)
            .AddSource("Wolverine")
            // Custom sources created by modules/services
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
            // SetDbStatementForText. Verify which db.* attributes it emits
            // before treating the production configuration as PII-safe.
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
            // Wolverine's meter is named "Wolverine:{ApplicationName}", so a wildcard is required
            .AddMeter("Wolverine*")
            // Custom meters, explicitly (do not rely on wildcards)
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
```

`BaggageMiddleware.cs`:

```csharp
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;

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
```

Important: middleware registration belongs in `UseSharedPipeline()`, not `Program.cs` (the architecture rule is that `Program.cs` only chains `Add*` / `Use*`).

---

## 4. Wire each host

Add the project reference to every host csproj:

```xml
<ProjectReference Include="..\..\Shared\MyApp.Observability\MyApp.Observability.csproj" />
```

Add one call to each `Program.cs` (inside the existing chain):

```csharp
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMyAppObservability(builder.Configuration, "myapp.apihost")
    .AddApi(builder.Configuration);
```

| Host | `serviceName` |
| --- | --- |
| Gateway | `myapp.gateway` |
| ApiHost | `myapp.apihost` |
| InvoicesApi | `myapp.invoicesapi` |
| OrdersApi | `myapp.ordersapi` |
| ShippingApi | `myapp.shippingapi` |

Add `using MyApp.Observability;` to each host’s `Presentation/WebApplicationExtensions.cs`, then add the endpoint to each host’s `.env` (ApiHost already loads `.env` via `DotNetEnv`; for services, use `appsettings.Development.json` or shell env):

```env
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_DEPLOYMENT_ENVIRONMENT=dev
```

Register the baggage middleware inside `Presentation/WebApplicationExtensions.cs`:

```csharp
public static WebApplication UseSharedPipeline(this WebApplication app)
{
    // ... existing exception handling, auth, etc.

    app.UseMiddleware<BaggageMiddleware>();

    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapOpenApi().AllowAnonymous();

    return app;
}
```

---

## 5. Wolverine and RabbitMQ correlation

No package and no options are needed. Wolverine already emits activities on its `"Wolverine"` source and writes `traceparent` into RabbitMQ headers.

What you must do:

1. Register the source: `.AddSource("Wolverine")` (done in §3).
2. Register the meter with a wildcard: `.AddMeter("Wolverine*")` (done in §3), because the meter is named `Wolverine:{ApplicationName}`.

What you get:

- Producer span on publish (`ActivityKind.Producer`).
- Consumer span on receive (`ActivityKind.Consumer`).
- Both in the **same trace**, with a parent/child relationship through the envelope’s parent id.

Do not expect a “Linked traces” panel for RabbitMQ fan-out. That panel is for span links, which Wolverine does not use here. In Tempo you will see one trace with several services.

Baggage propagation through Wolverine depends on the Wolverine version and transport. For dev, treat `user.id` in consumer logs as best-effort. If it does not appear, read the user from your message payload or a database lookup — do not block the quickstart on it.

---

## 6. Add custom metrics and spans

Auto-instrumentation gives you HTTP, HttpClient, EF Core and Wolverine spans. Business context must be added explicitly.

### 6.1 Catalog (ApiHost module)

```csharp
// src/Modules/Catalog/Products/CatalogTelemetry.cs
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace MyApp.Modules.Catalog.Products;

public static class CatalogTelemetry
{
    public static readonly ActivitySource Source = new("MyApp.Catalog", "1.0.0");
    public static readonly Meter Meter = new("MyApp.Catalog", "1.0.0");

    public static readonly Counter<long> ProductsCreated =
        Meter.CreateCounter<long>("myapp.catalog.products_created");

    public static readonly Histogram<double> CreateDuration =
        Meter.CreateHistogram<double>("myapp.catalog.create_duration", unit: "ms");
}
```

Use it in the create handler:

```csharp
using var activity = CatalogTelemetry.Source.StartActivity("CreateProduct");
activity?.SetTag("myapp.module", "catalog");
activity?.SetTag("myapp.slice", "create_product");
var started = Stopwatch.GetTimestamp();

var created = Product.Create(command.Name, command.Price, command.Stock, category, brand);
if (created.IsError)
{
    activity?.SetStatus(ActivityStatusCode.Error, "Product.Create returned errors");
    return ErrorOr<Guid>.From(created.Errors.ToList());
}

dbContext.Products.Add(created.Value);

activity?.AddEvent(new ActivityEvent("domain.event.raised", DateTimeOffset.UtcNow,
    new ActivityTagsCollection { { "event.type", "ProductCreated" } }));
CatalogTelemetry.ProductsCreated.Add(1, new KeyValuePair<string, object?>("category", command.Category));
CatalogTelemetry.CreateDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds);

activity?.SetStatus(ActivityStatusCode.Ok);
return created.Value.Id;
```

The meter and source are registered explicitly in §3. No wildcard required.

### 6.2 OrdersApi (UpDownCounter example)

```csharp
// src/Services/MyApp.OrdersApi/Orders/OrderTelemetry.cs
public static class OrderTelemetry
{
    public static readonly ActivitySource Source = new("MyApp.Orders", "1.0.0");
    public static readonly Meter Meter = new("MyApp.Orders", "1.0.0");

    public static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("myapp.orders.created");

    public static readonly UpDownCounter<int> ActiveOrders =
        Meter.CreateUpDownCounter<int>("myapp.orders.active");
}
```

In `PlaceOrderCommandHandler`:

```csharp
OrderTelemetry.OrdersCreated.Add(1, new KeyValuePair<string, object?>("status", "placed"));
OrderTelemetry.ActiveOrders.Add(1, new KeyValuePair<string, object?>("status", "placed"));
```

`UpDownCounter` is correct for “currently in state X”. A `Counter` cannot decrement, so `created - shipped` drifts after a restart.

---

## 7. Map `ErrorOr` failures to span status

This behavior belongs in `Shared.Application` and uses only `System.Diagnostics` (no OpenTelemetry package needed there).

```csharp
// src/Shared/Application/Behaviors/ErrorTracingBehavior.cs
using System.Diagnostics;
using Mediator;
using Shared.Common.Errors;

namespace Shared.Application.Behaviors;

public sealed class ErrorTracingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken ct)
    {
        var response = await next(ct);

        if (response is IErrorOr { IsError: true })
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "ErrorOr failure");
        else
            Activity.Current?.SetStatus(ActivityStatusCode.Ok);

        return response;
    }
}
```

Register it after `TransactionBehavior`:

```csharp
services.AddPipelineBehavior(typeof(ErrorTracingBehavior<,>));
```

This marks the **handler’s** span red. It does not change the HTTP status. A 400 response remains a 400; only the span status is `Error`. If you want the HTTP span flagged too, set the status in the endpoint from the result — do not write a second mapping layer.

If you want error codes on the span, add `IReadOnlyList<Error> Errors` to `IErrorOr` (the interface currently exposes only `IsError`). That is a one-line change in `Shared.Common` and is safe because `ErrorOr<T>` already implements it.

---

## 8. Generate traffic

```bash
# 1. Login
TOKEN=$(curl -s -X POST http://localhost:5101/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"Email":"admin@example.com","Password":"ChangeMe-12345"}' \
  | jq -r .access_token)

# 2. Create a product -> ApiHost + Search + OrdersApi in one trace.
#    The 201 body is the new product id.
PRODUCT_ID=$(curl -s -H "Authorization: Bearer $TOKEN" \
  -X POST http://localhost:5101/products \
  -H 'Content-Type: application/json' \
  -d '{"Name":"Telemetry Test","Category":"Books","Price":12.99,"Stock":5}' \
  | tr -d '"')

# 3. Read a product to generate a GET span
curl -s -H "Authorization: Bearer $TOKEN" \
  "http://localhost:5101/products?pageSize=5" > /dev/null

# 4. Place an order -> OrdersApi + InvoicesApi + ShippingApi in one trace
curl -s -H "Authorization: Bearer $TOKEN" \
  -X PUT http://localhost:5103/cart/items \
  -H 'Content-Type: application/json' \
  -d "{\"productId\":\"$PRODUCT_ID\",\"quantity\":1}"

curl -s -H "Authorization: Bearer $TOKEN" -X POST http://localhost:5103/orders

sleep 3
```

To find the trace id, query Tempo — do not expect a `traceparent` response header:

```bash
curl -s "http://localhost:3200/api/search?tags=service.name%3Dmyapp.apihost&limit=1" \
  | jq -r '.traces[0].traceID'
```

Open that trace in Tempo. For `POST /products` you should see:

```
POST /products                  myapp.apihost
├── CreateProduct               myapp.apihost  (myapp.module=catalog)
├── savechanges                 myapp.apihost
└── send ProductCreated         myapp.apihost
    ├── receive ProductCreated  myapp.apihost  (Search module)
    └── receive ProductCreated  myapp.ordersapi
```

The `service.name` on a custom span is the host's resource, not the module name. Use the `myapp.module` tag to tell Catalog spans apart from the rest of ApiHost.



For the order, you should see `myapp.ordersapi`, `myapp.invoicesapi`, and `myapp.shippingapi`.

---

## 9. Verify each component

```bash
# Collector is receiving OTLP
curl -s http://localhost:8889/metrics | grep otelcol_receiver_accepted

# Prometheus is up and has .NET metrics
curl -s "http://localhost:9090/api/v1/query?query=up" | jq
curl -s "http://localhost:9090/api/v1/query?query=http_server_request_duration_seconds_count" \
  | jq '.data.result | length'

# Loki is ready and has logs (label uses an underscore)
curl -s http://localhost:3100/ready
curl -s -G "http://localhost:3100/loki/api/v1/query_range" \
  --data-urlencode 'query={service_name="myapp.apihost"}' \
  --data-urlencode 'limit=3' | jq '.data.result | length'

# Tempo is ready and has traces
curl -s http://localhost:3200/ready
curl -s "http://localhost:3200/api/search?tags=service.name%3Dmyapp.apihost&limit=3" | jq '.traces'

# Grafana and datasources
curl -s http://localhost:3000/api/health
curl -s -u admin:admin http://localhost:3000/api/datasources \
  | jq '.[] | {name, type, uid}'

# Alertmanager
curl -s http://localhost:9093/-/healthy
```

Prometheus label names are sanitized: `service.name` becomes `service_name`, `http.route` becomes `http_route`, `http.status_code` becomes `http_status_code`. Use the sanitized names in PromQL.

```promql
# Request rate per route
sum by (http_route) (rate(http_server_request_duration_seconds_count{service_name="myapp.apihost"}[5m]))

# p95 latency per route
histogram_quantile(0.95,
  sum by (http_route, le) (rate(http_server_request_duration_seconds_bucket[5m])))

# Business counter
sum (myapp_catalog_products_created_total)

# Orders currently placed
myapp_orders_active{status="placed"}
```

---

## 10. Grafana tour

Open http://localhost:3000 (`admin` / `admin`).

1. **Metrics:** Explore → Prometheus → run the queries above.
2. **Logs:** Explore → Loki → `{service_name="myapp.apihost"}` → Logs view.
3. **Traces:** Explore → Tempo → search `service.name = myapp.apihost`.
4. **Trace → logs:** open a trace, then “Logs for this trace”. This works because the OTel logs SDK attaches `trace_id`.
5. **Log → trace:** expand a log line and click “View trace”. This uses the `trace_id` structured field configured in §1.6.

If the log→trace link does not appear, check that Loki received `trace_id` as structured metadata:

```bash
curl -s -G "http://localhost:3100/loki/api/v1/query_range" \
  --data-urlencode 'query={service_name="myapp.apihost"} | trace_id != ""' \
  --data-urlencode 'limit=1' | jq
```

---

## 11. Common gotchas

| Symptom | Cause | Fix |
| --- | --- | --- |
| No custom spans | Source name not registered | Add the exact `ActivitySource` name in `AddSource(...)`. Wildcards for sources are version-dependent; use explicit names. |
| No Wolverine spans | `"Wolverine"` source not registered | Add `.AddSource("Wolverine")`. |
| No Wolverine metrics | Meter registered exactly | Use `.AddMeter("Wolverine*")`; the meter is `Wolverine:{AppName}`. |
| No logs in Loki | Collector pointed at `loki:4317` | Use `otlphttp/loki` with `http://loki:3100/otlp`. |
| No metrics in Prometheus | Scraping the app instead of the collector | Scrape `otel-collector:8889`. |
| `service_name` missing in PromQL | Resource conversion disabled | Set `resource_to_telemetry_conversion.enabled: true` in the collector. |
| Traces missing after a while | Tempo retention | Dev retention is 48h; it is not a bug. |
| `traceparent` header not in curl response | ASP.NET Core does not emit it | Read the trace id from Tempo, not from the response. |
| Consumer trace missing | The consumer service is not running | Start OrdersApi/InvoicesApi/ShippingApi; check `/health`. |
| `ErrorOr` failures are green | Handler span is not the HTTP span | Expected. The behavior marks the handler span red; the HTTP status is unchanged. |
| Integration tests time out on messaging | Manually started hosts hold the RabbitMQ queue consumers | Stop the `dotnet run` hosts before `dotnet test tests/YourApp.Tests.Integration`. |

---

## 12. Tear down

```bash
cd observability-dev
docker compose down        # keep data
docker compose down -v     # delete data
```

The .NET side is additive: remove the `AddMyAppObservability` calls, the project reference, and the package versions to revert.

---

## 13. Cheat sheet

```bash
cd observability-dev && docker compose up -d

# Hosts
dotnet run --project src/Services/MyApp.Gateway      # 5100
dotnet run --project src/Services/MyApp.ApiHost      # 5101
dotnet run --project src/Services/MyApp.InvoicesApi  # 5102
dotnet run --project src/Services/MyApp.OrdersApi    # 5103
dotnet run --project src/Services/MyApp.ShippingApi  # 5104

# Traffic
TOKEN=$(curl -s -X POST http://localhost:5101/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"Email":"admin@example.com","Password":"ChangeMe-12345"}' | jq -r .access_token)
curl -s -H "Authorization: Bearer $TOKEN" -X POST http://localhost:5101/products \
  -H 'Content-Type: application/json' \
  -d '{"Name":"Cheat Sheet","Category":"Books","Price":9.99,"Stock":1}'

# Trace id
curl -s "http://localhost:3200/api/search?tags=service.name%3Dmyapp.apihost&limit=1" \
  | jq -r '.traces[0].traceID'
```

---

## 14. What this guide does not cover

These are deliberate exclusions, not omissions:

- Production topology (agent + gateway collectors, TLS, queue persistence).
- Sampling (head or tail).
- PII redaction beyond SQL text.
- SLOs, alerts, and error budgets.
- Retention, backup, HA, and cost.
- The synthetic health probe.
- Multi-tenancy.

---

## Appendix — verified versions

These versions were resolved from the local NuGet cache and match `net10.0`:

| Package | Version |
| --- | --- |
| `OpenTelemetry` | `1.17.0` |
| `OpenTelemetry.Extensions.Hosting` | `1.17.0` |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.17.0` |
| `OpenTelemetry.Instrumentation.AspNetCore` | `1.17.0` |
| `OpenTelemetry.Instrumentation.Http` | `1.17.0` |
| `OpenTelemetry.Instrumentation.Runtime` | `1.15.1` |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | `1.13.0-beta.1` |
| `WolverineFx` (existing) | `6.51.2` |
| Collector contrib | `0.110.0` |
| Prometheus | `v2.55.1` |
| Loki | `3.2.1` |
| Tempo | `2.6.0` |
| Grafana | `11.3.0` |
| Alertmanager | `v0.27.0` |

`OpenTelemetry.Instrumentation.EntityFrameworkCore` is still beta; it emits spans, not metrics. Do not build dashboards on EF Core metrics.
