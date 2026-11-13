# OpenTelemetry Dev Quickstart

> **What this is:** a minimal, runnable observability setup for local development — one collector, the Grafana stack, and all five .NET hosts (Gateway, ApiHost, OrdersApi, InvoicesApi, ShippingApi).
>
> **What this is not:** a production design. No HA, no TLS, no sampling policy, no retention policy, no SLOs. Those are a separate concern.

**How to use this guide (learning order):**

1. §0–§1: understand the topology and start the backends. Every YAML file has a “what it does” paragraph and a “key settings” table before the file.
2. §2–§3: add the packages and the one shared observability project. Read the “what each block does” table.
3. §4–§5: wire the five hosts and understand Wolverine’s parent/child trace model.
4. §6–§7: add business metrics and map `ErrorOr` failures to span status.
5. §8–§10: generate traffic and read the result in Grafana.

Commands show the directory they expect. The short version: compose files are under `MyApp/`; `dotnet run` commands are under `MyApp/backend/`.

> **Verified:** the Docker stack in this guide was started, ApiHost + OrdersApi + InvoicesApi + ShippingApi were run, and both traces described below were observed in Tempo. Logs arrived in Loki with `trace_id`, metrics arrived in Prometheus, and the custom Catalog counter appeared. The Gateway wiring is included; its proxy spans are not part of the verified flow because it only forwards. The code in §3–§7 is in the repo.

---

## 0. Topology (what actually happens)

**The three signals in one sentence each:**

| Signal | It answers | Stored in | What you will look at |
| --- | --- | --- | --- |
| Metrics | “How many? How fast?” | Prometheus | Rates, latency percentiles, error rate |
| Traces | “What happened in this one request?” | Tempo | The span tree, including cross-service hops |
| Logs | “What did the code say?” | Loki | The exact log line, linked by `trace_id` |

Every signal carries `service.name` as its identity. That is the label you use to separate ApiHost, OrdersApi, InvoicesApi, ShippingApi, and the Gateway.


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

**What this file does:** it starts six containers and connects them on one Docker network. Think of it as the “machine room”: the collector is the door telemetry comes in through, and Prometheus/Loki/Tempo are the three storage rooms behind it. Grafana is the window you look through.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `otel-collector` image | Runs the OpenTelemetry Collector (contrib distribution) | `contrib` includes the Prometheus and Loki exporters we need |
| `ports: "4317:4317"` | Exposes OTLP/gRPC on the host | The .NET apps send traces, metrics, and logs here |
| `ports: "4318:4318"` | Exposes OTLP/HTTP | Useful for tools that prefer HTTP; the apps use gRPC |
| `ports: "8889:8889"` | Exposes the collector’s Prometheus endpoint | Prometheus scrapes metrics from here |
| `volumes: ...:ro` | Mounts config files read-only | The container cannot rewrite your config |
| `depends_on` | Starts containers in order | It does **not** wait for readiness; the collector retries until Tempo/Loki are ready |
| `restart: unless-stopped` | Restarts after a crash or reboot | Keeps a dev stack alive without manual restarts |
| `GF_SECURITY_ADMIN_*` | Grafana login | Dev-only `admin` / `admin`; never use this in production |
| Named volumes | Persist data between `down` and `up` | `docker compose down -v` deletes it |

**How the containers talk:**

```text
.NET apps ──OTLP 4317──► otel-collector ──► tempo:4317   (traces)
                                     ├──► loki:3100    (logs, OTLP HTTP)
                                     └──► :8889        (metrics, scraped by Prometheus)

Grafana ──► Prometheus :9090, Loki :3100, Tempo :3200, Alertmanager :9093
```


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

**What this file does:** it tells the collector how to **receive**, **process**, and **export** each signal. A collector config always has the same four blocks: `receivers`, `processors`, `exporters`, and `service.pipelines`.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `receivers.otlp.protocols.grpc` | Listens for OTLP/gRPC on `4317` | This is the port every .NET host sends to |
| `receivers.otlp.protocols.http` | Listens for OTLP/HTTP on `4318` | Fallback for tools that cannot use gRPC |
| `processors.memory_limiter` | Refuses data before the collector runs out of RAM | A collector that OOMs loses everything in flight |
| `processors.batch` | Groups spans/metrics/logs into larger sends | Fewer network calls; adds ~1s latency in dev |
| `exporters.prometheus` | Exposes collected metrics as a Prometheus scrape target | Prometheus is pull-based; it needs an endpoint to scrape |
| `resource_to_telemetry_conversion` | Turns resource attributes such as `service.name` into metric labels | Otherwise all services collapse into one series |
| `exporters.otlp/tempo` | Sends traces to Tempo over OTLP/gRPC | Tempo listens on `4317` inside its container |
| `exporters.otlphttp/loki` | Sends logs to Loki over OTLP/HTTP at `/otlp` | Loki’s OTLP **HTTP** endpoint is `:3100`, not `:4317` |
| `exporters.debug` | Prints telemetry to the collector log | Useful while learning; remove it when the output gets noisy |
| `service.pipelines` | Wires receiver → processors → exporters per signal | Traces, metrics, and logs each have their own path |

**Pipeline order matters:** `memory_limiter` runs first (protect the process), then `batch` (reduce network calls), then export. If you reverse them, a memory spike can still drop data before batching.


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

**What this file does:** Prometheus is **pull-based**. It does not receive metrics; it asks an endpoint for them every `scrape_interval`. Here it asks the collector for the metrics the collector received from your apps.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `global.scrape_interval: 15s` | How often Prometheus scrapes | Matches the metrics export interval in the .NET host (15s) |
| `job_name: otel-collector` | A logical name for the target | Appears as the `job` label in PromQL |
| `targets: ['otel-collector:8889']` | The collector’s Prometheus exporter | This is the only endpoint Prometheus needs in this dev setup |

We do **not** scrape the .NET apps directly. They push OTLP; the collector converts and exposes the metrics. Adding a direct app scrape would create a second, different metrics path and duplicate data.


```yaml
global:
  scrape_interval: 15s

scrape_configs:
  - job_name: otel-collector
    static_configs:
      - targets: ['otel-collector:8889']
```

### 1.4 `loki.yml`

**What this file does:** Loki stores logs. The important part is how OTel log records become Loki streams you can filter by service.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `auth_enabled: false` | Disables Loki authentication | Dev only; never expose this in production |
| `server.http_listen_port: 3100` | HTTP port for queries and OTLP/HTTP ingest | The collector sends logs to `http://loki:3100/otlp` |
| `common.storage.filesystem` | Stores chunks on disk | Simple dev setup; production uses object storage |
| `distributor.otlp_config.default_resource_attributes_as_index_labels` | Chooses which OTel resource attributes become Loki labels | `service.name` is the one you filter by; keep this list short |
| `limits_config.allow_structured_metadata: true` | Allows fields such as `trace_id` to be stored as metadata | Needed for the log → trace link in Grafana |
| `retention_period: 168h` | Deletes logs after 7 days | Dev retention; production needs a real policy |
| `schema_config` / `tsdb` | Uses Loki’s current TSDB index | Required for structured metadata and modern Loki |

**Why label cardinality matters:** every unique label combination creates a stream. `service.name` is low-cardinality and safe. `trace_id` is **not** a label; it is stored as structured metadata, which is why the Grafana link uses `matcherType: label` on `trace_id` rather than making it a stream label.


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

**What this file does:** Tempo stores traces by trace id. It is not a search engine over every attribute; it stores the trace tree and lets Grafana query it.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `server.http_listen_port: 3200` | Tempo’s HTTP API | Grafana and `curl` query traces here |
| `distributor.receivers.otlp.protocols.grpc` | Accepts OTLP/gRPC on `4317` | The collector sends traces here |
| `storage.trace.backend: local` | Stores blocks on disk | Dev only; production uses object storage (S3/GCS) |
| `storage.trace.wal` | Write-ahead log for in-flight traces | Lets Tempo recover after a restart |
| `compactor.compaction.block_retention: 48h` | Deletes trace blocks after 48 hours | Dev retention; production is usually 7–30 days or sampled |

Tempo needs a little time before a new trace is searchable. If a trace is missing immediately after a request, wait a few seconds and query again.


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

**What this file does:** it provisions the four datasources and, more importantly, the **links between them**. The links are what turn three separate tools into one investigation flow.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `uid` on each datasource | A stable id | Links reference datasources by `uid`, not by display name |
| Prometheus `isDefault: true` | The default datasource in Explore | Most investigations start with a metric |
| Loki `derivedFields` | Extracts a field from a log record and links to Tempo | `trace_id` is a structured field, so `matcherType: label` is correct |
| `matcherType: label` | Tells Grafana to read a label/metadata field | A regex over the message body would not match the OTel log format |
| Tempo `tracesToLogsV2` | Adds a “Logs for this trace” button | The reverse pivot: trace → logs |
| `filterByTraceID: true` | Filters Loki by the selected trace | Without it, the log panel shows unrelated lines |
| Alertmanager datasource | Lets Grafana show alerts | Optional for the tracing tour, but provisioned for later |

**The three pivots you get:**

1. Metric → trace: requires exemplars (not enabled in this dev setup).
2. Trace → log: Tempo → Loki, using the trace id.
3. Log → trace: Loki → Tempo, using the `trace_id` field.


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

**What this file does:** Alertmanager receives alerts from Prometheus and routes them to receivers (Slack, PagerDuty, a webhook). In dev we route everything to an empty receiver.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `route.receiver: dev-null` | The default route | Every alert goes to one no-op receiver |
| `receivers: - name: dev-null` | A receiver with no configuration | Valid in Alertmanager; it acknowledges without sending anywhere |

In production you replace `dev-null` with real receivers and add routes by severity. Do not leave an empty receiver in production; alerts would disappear silently.


```yaml
route:
  receiver: dev-null

receivers:
  - name: dev-null
```

### 1.8 Start the backends

From the `MyApp/` folder:

```bash
# 1. RabbitMQ + Elasticsearch (the app's own dependencies)
docker compose -f backend/docker-compose.yml up -d

# 2. The observability stack
docker compose -f observability-dev/docker-compose.yml up -d
docker compose -f observability-dev/docker-compose.yml ps
```

All six observability containers should be running, and RabbitMQ/Elasticsearch should become healthy. If the collector restarts, run:

```bash
docker compose -f observability-dev/docker-compose.yml logs otel-collector --tail 30
```

---

## 2. Add the packages

Every package below answers a different question:

| Package | It gives you | Why you need it |
| --- | --- | --- |
| `OpenTelemetry.Extensions.Hosting` | `AddOpenTelemetry()` for ASP.NET Core | Starts and stops the SDK with the host |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | OTLP export | Sends all three signals to the collector |
| `OpenTelemetry.Instrumentation.AspNetCore` | HTTP server spans and metrics | Every request gets a span and latency histogram |
| `OpenTelemetry.Instrumentation.Http` | Outbound `HttpClient` spans | Shows calls the app makes to other services |
| `OpenTelemetry.Instrumentation.Runtime` | GC, thread pool, memory metrics | Explains “slow because the runtime is struggling” |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | EF Core spans | Shows database work under the request span |

The EF package is still beta. It emits **traces**, not metrics, and it does not expose the old `SetDbStatementForText` switch. Verify its attributes before treating it as PII-safe.


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

**Why a project instead of copying a file into each host?** Telemetry is host-level, but the same wiring is needed by five hosts. One shared project keeps the configuration identical and prevents “OrdersApi has a different sampler” drift.

**Why not put it in `MyApp.Modular`?** Every module references `MyApp.Modular`. Adding OpenTelemetry there would force the SDK into modules that do not need it and grow the shared kernel. `MyApp.Observability` is referenced by hosts only.


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

**What each block does:**

| Code | Meaning | Why it is there |
| --- | --- | --- |
| `configured endpoint` | Reads `OTEL_EXPORTER_OTLP_ENDPOINT` | One environment variable points every host at the collector |
| `ConfigureResource` | Sets `service.name`, version, environment, host | Identity is what makes `service_name` filterable everywhere |
| `AddSource("Wolverine")` | Subscribes to Wolverine’s activities | Without a listener, `StartActivity` returns `null` and no span is exported |
| `AddSource("MyApp.Catalog")` etc. | Subscribes to your custom sources | Sources are matched by exact name; there is no reliable wildcard for sources |
| `AddAspNetCoreInstrumentation` | Creates server spans and HTTP metrics | This is the root span for every request |
| `Filter` on `/health`, `/openapi` | Drops noisy requests | Health checks would otherwise dominate the trace list |
| `AddHttpClientInstrumentation` | Creates outbound spans | Shows downstream calls inside the request trace |
| `AddEntityFrameworkCoreInstrumentation` | Creates DB spans | Shows query time under the handler span |
| `AddOtlpExporter` (traces) | Sends spans to the collector | The collector routes them to Tempo |
| `AddMeter("Wolverine*")` | Subscribes to Wolverine’s meter | The meter name is `Wolverine:{AppName}`, so a wildcard is required |
| `AddMeter("MyApp.Catalog")` etc. | Subscribes to your custom meters | Exact names only; this is why each module’s meter is listed |
| `reader...ExportIntervalMilliseconds` | Sends metrics every 15s | Matches the Prometheus scrape interval |
| `AddLogging(...AddOpenTelemetry...)` | Sends `ILogger` records over OTLP | Logs then carry `trace_id` and `span_id` |

**Why no manual propagator:** OpenTelemetry’s default propagator is already W3C TraceContext + Baggage. Setting it in code is unnecessary, and in 1.17 the static property is read-only through the DI builder. Use `OTEL_PROPAGATORS` only if you need a non-default one.

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

**Why one line per host?** `Program.cs` should only compose. The `Add*` call is the composition; everything else lives in the shared project. If you put `AddOpenTelemetry` blocks in `Program.cs`, the host stops being a thin composition root.

**Why a different `serviceName` per host?** `service.name` is how you tell ApiHost from OrdersApi in Tempo, Loki, and Prometheus. If every host reported the same name, you could not filter or build per-service dashboards.

**Why middleware after authentication?** `BaggageMiddleware` reads `context.User` claims. If it runs before `UseAuthentication`, the user is not populated yet and `user.id` is missing.


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

### 4.1 Run the hosts (in this order)

Do this **before** generating traffic. The order matters because ApiHost owns the database schema, seed data, and the integration-event wiring that the extracted services consume.

From `MyApp/backend/`, open five terminals:

```bash
# Terminal 1 - ApiHost first (schema + seed + exchanges/queues)
dotnet run --project src/Services/MyApp.ApiHost --urls http://localhost:5101

# Terminal 2 - OrdersApi (consumer of product events)
dotnet run --project src/Services/MyApp.OrdersApi --urls http://localhost:5103

# Terminal 3 - InvoicesApi (consumer of order events)
dotnet run --project src/Services/MyApp.InvoicesApi --urls http://localhost:5102

# Terminal 4 - ShippingApi (consumer of order events)
dotnet run --project src/Services/MyApp.ShippingApi --urls http://localhost:5104

# Terminal 5 - Gateway last (optional, proxy + Scalar)
dotnet run --project src/Services/MyApp.Gateway --urls http://localhost:5100
```

**Why this order:**

| Step | Why it is here |
| --- | --- |
| ApiHost first | Creates/migrates the module databases, seeds the admin user, and declares exchanges/queues |
| OrdersApi next | Starts the product-event consumer before you create a product |
| Invoices/Shipping next | Start the order-event consumers before you place an order |
| Gateway last | It only forwards; it does not own state |

Wait for each `/health` to return 200 before moving on:

```bash
for port in 5101 5103 5102 5104 5100; do
  curl -s -o /dev/null -w "$port -> %{http_code}\n" http://localhost:$port/health
done
```

If a service fails on startup, check its terminal. The usual causes are a missing RabbitMQ connection string or Elasticsearch not being ready yet.

**Important:** stop these hosts before running `dotnet test tests/MyApp.Tests.Integration`. The test hosts use the same durable RabbitMQ queues, and a manually running consumer will steal messages from the test.

---

## 5. Wolverine and RabbitMQ correlation

**What to understand before reading the config:** a trace is a tree of spans. Wolverine creates a **producer** span when a message is published and a **consumer** span when another process receives it. The consumer span uses the producer’s context as its parent, so both appear in the same trace. That is why one `POST /products` shows ApiHost and OrdersApi together.

**What you need to register:** only the source and meter. Wolverine writes `traceparent` into RabbitMQ headers itself.


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

**Why custom telemetry at all?** Auto-instrumentation can say “the request took 50ms” and “a database query ran”. It cannot say “this was a Catalog product creation in the Books category”. Custom spans and metrics add that business context.

**Pick the right instrument:**

| Instrument | It can | Use it for |
| --- | --- | --- |
| `Counter<long>` | Only go up | “How many products were created?” |
| `Histogram<double>` | Record a distribution | “How long did creation take?” |
| `UpDownCounter<int>` | Go up and down | “How many orders are currently placed?” |

A `Counter` cannot decrement. If you try to model “currently active” as `created - shipped`, the number drifts after a restart. Use `UpDownCounter`.

**Why register meter names explicitly?** OpenTelemetry matches meters and sources by name. Wildcards are version-dependent, and a typo silently produces no data. The one exception is Wolverine, whose meter includes the application name.


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

**What this behavior does:** it looks at the handler’s `ErrorOr<T>` result and sets the **span status** to `Error` or `Ok`. It does not change the HTTP status code.

**Why that distinction matters:** a `404 Product not found` is a normal HTTP response. The endpoint still returns 404, but the trace span is marked red so you can filter business failures in Tempo. If you also want the HTTP span marked, set it in the endpoint from the result; do not add a second mapping layer.

**Scope:** this marks the handler span. The HTTP server span is the root and normally stays green for a 4xx response. That is expected.


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

**Prerequisite:** the hosts from §4.1 are running and their `/health` endpoints return 200. If they are not, start them first; traffic generation against stopped hosts produces no telemetry.

**What each request demonstrates:**

| Request | What it shows |
| --- | --- |
| `POST /auth/login` | An HTTP server span, an EF Core span, and logs with a trace id |
| `POST /products` | ApiHost → RabbitMQ → Search (in-process) and OrdersApi, plus the custom `CreateProduct` span and the product counter |
| `GET /products` | A read-only request with no write transaction |
| `PUT /cart/items` | A command in OrdersApi with its own trace |
| `POST /orders` | OrdersApi → RabbitMQ → InvoicesApi and ShippingApi, with the order counter and active-order gauge |

**How to read the trace tree:** the top line is the incoming HTTP request. Indented lines are work done because of it. A Wolverine `send` is followed by `receive` spans in the consumer processes. All of them share one `trace_id`.


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

Each check proves one link in the chain. Work from the app outward:

| Check | What it proves |
| --- | --- |
| Collector `/metrics` | The apps reached the collector and it accepted OTLP |
| Prometheus `up` | Prometheus can scrape the collector |
| Prometheus `http_server_request_duration_seconds_count` | Metrics were converted and labelled correctly |
| Loki `{service_name="..."}` | Logs arrived and `service.name` became a stream label |
| Tempo `/api/search` | Spans arrived and are queryable |
| Grafana `/api/health` | The UI is up and datasources are provisioned |
| Alertmanager `/-/healthy` | The alerting component is up |

Prometheus sanitizes label names: `service.name` becomes `service_name`, `http.route` becomes `http_route`. Use the underscore form in PromQL, not the OTel form.


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

**Explore is the investigation surface.** Each datasource gives you a different starting point:

| Start with | Ask | Then pivot |
| --- | --- | --- |
| Prometheus | “Which route got slower?” | Open a trace if exemplars are enabled |
| Tempo | “What happened in this request?” | “Logs for this trace” |
| Loki | “What did the code log?” | “View trace” using the `trace_id` field |

The links only work if the datasource `uid`s and the `trace_id` field are configured correctly. That is why §1.6 uses stable `uid`s and `matcherType: label`.


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

From the `MyApp/` folder:

```bash
# Observability stack
docker compose -f observability-dev/docker-compose.yml down      # keep data
docker compose -f observability-dev/docker-compose.yml down -v   # delete data

# RabbitMQ + Elasticsearch
docker compose -f backend/docker-compose.yml down
```

The .NET side is additive: remove the `AddMyAppObservability` calls, the project reference, and the package versions to revert.

---

## 13. Cheat sheet

```bash
# ─── From MyApp/ ───────────────────────────────────────────
docker compose -f backend/docker-compose.yml up -d
docker compose -f observability-dev/docker-compose.yml up -d

# ─── From MyApp/backend/ (separate terminals) ──────────────
dotnet run --project src/Services/MyApp.ApiHost     --urls http://localhost:5101
dotnet run --project src/Services/MyApp.OrdersApi   --urls http://localhost:5103
dotnet run --project src/Services/MyApp.InvoicesApi --urls http://localhost:5102
dotnet run --project src/Services/MyApp.ShippingApi --urls http://localhost:5104
dotnet run --project src/Services/MyApp.Gateway     --urls http://localhost:5100

# ─── Traffic ───────────────────────────────────────────────
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
