# OpenTelemetry Concepts — Coverage Audit & Architecture Patterns

> **Companion to:** [`TELEMETRY.md`](./TELEMETRY.md) (prod guide) and [`TELEMETRY-DEV-QUICKSTART.md`](./TELEMETRY-DEV-QUICKSTART.md) (dev crash course).
>
> **Purpose:** Audit which OpenTelemetry concepts from the *"From Zero to Hero: OpenTelemetry in .NET"* course are covered in the existing docs, then fill every gap with a concrete pattern wired to the MyApp kit (modular monolith + Wolverine/RabbitMQ + EF Core + Carter + `ErrorOr<T>`).
>
> **Read this if:** you've taken (or are taking) the course and want to see how each concept lands in a real .NET 10 modular architecture — not a toy console app.

---

## Part A — Coverage matrix

Concepts extracted from the course curriculum, ignoring meta-lessons (Demo, Preview, Recap). Updated after the patterns were wired into `TELEMETRY.md` and `TELEMETRY-DEV-QUICKSTART.md`.

**Status legend:**
- **✓ wired** — code is integrated into TELEMETRY.md (TM) and/or DEV-QUICKSTART.md (DQ), ready to use
- **✓ concept** — conceptual explanation in this doc (Parts B–F); no code needed (it's a "what is" definition)
- **✓ recipe** — optional pattern with full code in this doc; adopt only when your architecture needs it

| # | OTel concept | TM section | DQ section | Status | Learn here |
|---|---|---|---|---|---|
| 1 | Observability (vs monitoring) | §1 | §0 | ✓ concept | B.1 |
| 2 | OpenTelemetry (project / spec / API / SDK) | throughout | throughout | ✓ concept | B.2 |
| 3 | Three signals: traces, metrics, logs | §8, §9, §11 | §6 | ✓ concept | B.3 |
| 4 | Context & correlation (trace_id pivot) | §9.2, §10, §19 | §6.4 | ✓ wired | — |
| 5 | Automatic instrumentation | §7.1 | §3.3 | ✓ wired | — |
| 6 | .NET terminology (ActivitySource, Meter) | §7.1, §8.5 | §3.3 | ✓ concept | C.1 |
| 7 | Resource (identity attributes) | §7.1 | §3.3 | ✓ wired | C.2 |
| 8 | Manual instrumentation | §8.5, §9.3 | — | ✓ wired | — |
| 9 | Instrumentation libraries (what they are) | §7.1 | §3.3 | ✓ concept | C.3 |
| 10 | What is a trace | §9 | — | ✓ concept | B.4 |
| 11 | Adding tracing | §7.1, §9.3 | §3.3 | ✓ wired | — |
| 12 | Exporters (Jaeger / Tempo / OTLP) | §6.2 | §2.2 | ✓ concept | C.4 |
| 13 | Span attributes (additional context) | §9.3 | — | ✓ wired | — |
| 14 | What is a metric | §8 | §6.1 | ✓ concept | B.5 |
| 15 | Instrument types (Counter/Histogram/Gauge/UpDownCounter) | §8.5, §8.7 | — | ✓ wired | C.5 |
| 16 | Adding metrics | §8.5 | — | ✓ wired | — |
| 17 | Exporting to Prometheus | §6.2, §13 | §2.2 | ✓ wired | — |
| 18 | .NET Meters | §8.5 | — | ✓ wired | — |
| 19 | Visualising on Grafana | §15.4, §19.6 | §6 | ✓ wired | — |
| 20 | OTLP protocol | §2 | §2.1 | ✓ concept | B.6 |
| 21 | What is the Collector | §6 | §2.2 | ✓ wired | — |
| 22 | Collector architecture (agent/gateway) | §0, §6 | §0 | ✓ wired | — |
| 23 | Running the Collector | §5, §17 | §2 | ✓ wired | — |
| 24 | Receivers | §6.1 | §2.2 | ✓ wired | — |
| 25 | Exporters (collector-side) | §6.2 | §2.2 | ✓ wired | — |
| 26 | Processors (batch, memory, filter, tail_sampling) | §6.1, §6.2 | §2.2 | ✓ wired | — |
| 27 | What is a log | §11 | — | ✓ concept | B.7 |
| 28 | Why logs for observability | — | — | ✓ concept | B.7 |
| 29 | Adding logging (OTel logs SDK) | §7.1 | §3.3 | ✓ wired | — |
| 30 | Exporting to Loki | §6.2, §11 | §2.2 | ✓ wired | — |
| 31 | Log enrichment | §7.1 | — | ✓ wired | — |
| 32 | **Baggage** | §7.1, §7.5a | §3.3, §3.7 | ✓ wired | D.1 |
| 33 | **Adding baggage** | §7.5a | §3.7 | ✓ wired | D.2 |
| 34 | **Custom context propagator** | — | — | ✓ recipe | D.3 |
| 35 | **Propagation risks** | §7.5a | §3.7 | ✓ wired | D.4 |
| 36 | **Custom span processor** | §7.1, §7.5b | — | ✓ wired | E.1 |
| 37 | Manually creating spans | §8.5, §21.4 | — | ✓ wired | — |
| 38 | **Span events** | §8.5, §9.4 | — | ✓ wired | E.2 |
| 39 | **Errors & exceptions in spans** | §7.5d, §9.5 | §3.7 | ✓ wired | E.3 |
| 40 | **Span links (manual)** | §9.6 | — | ✓ wired | E.4 |
| 41 | What is sampling | §6.2, §7.5c | — | ✓ concept | F.1 |
| 42 | **Head sampling** | §7.1, §7.5c | — | ✓ wired | F.2 |
| 43 | **Custom sampler** | §7.5c | — | ✓ wired | F.3 |
| 44 | Tail-based sampling | §6.2 | — | ✓ wired | — |
| 45 | Configuring tail sampling | §6.2 | — | ✓ wired | — |

**Coverage of the course syllabus: 45 / 45.** 32 have code in the practical docs · 12 are conceptual · 1 is an optional recipe. This is **not** a claim of production coverage: the course teaches instrumentation, not operating a telemetry backend. See §G for the production concepts that are still missing.

**Status note:** the practical docs were rewritten to match the current code (built-in Wolverine tracing, correct ports, correct consumer fan-out). Anything marked “wired” below means “there is a sample for this”, not “this has been deployed and verified”.

---

## Part B — Foundations (the "what is" gaps)

### B.1 What is observability?

**Monitoring** tells you *when* something is wrong (alerts on symptoms).
**Observability** tells you *why* something is wrong (you can ask arbitrary questions of the system without deploying new code).

The difference is whether you can answer a question you didn't think to ask in advance. Monitoring pre-declares the questions ("is CPU > 80%?", "is error rate > 1%?"). Observability preserves enough structured context (trace_id, service, route, user, db.statement, downstream calls) that you can pivot from symptom to root cause interactively.

**In the MyApp kit**, observability means: when a `POST /products` returns 500, you can click from the failing HTTP span → the EF Core `SaveChanges` span underneath → the Loki log line that has the same `trace_id` → the Wolverine message that was published → the OrdersApi consumer that failed downstream. Without redeploying anything.

The three signals serve different question types:

| Question | Signal |
|---|---|
| "Is something wrong? How fast? How often?" | **Metrics** (cheap, aggregated, always-on) |
| "What exactly happened in this one request?" | **Traces** (expensive, per-request, sampled) |
| "What did the code say at the time?" | **Logs** (structured text, always-on, indexed by trace_id) |

### B.2 What is OpenTelemetry?

OpenTelemetry (OTel) is **not** a backend, not a vendor, not a dashboard. It's a CNCF project with four deliverables:

| Layer | What it is | You consume it as |
|---|---|---|
| **Specification** | The contract: what a Span, Metric, Log, Resource, Baggage must look like | Read-only — defines correctness |
| **API** | The interface you code against (`Tracer`, `Meter`, `Logger`) | NuGet `OpenTelemetry.Api` |
| **SDK** | The implementation that wires API calls to exporters | NuGet `OpenTelemetry` + `OpenTelemetry.Extensions.Hosting` |
| **Collector** | A separate process that receives, processes, and exports telemetry | Docker image `otel/opentelemetry-collector-contrib` |

The key property: **vendor neutrality**. Your .NET code calls the OTel API; the SDK exports via OTLP; the Collector fans out to Prometheus, Loki, Tempo, Datadog, Honeycomb, Jaeger, or any combination. Switching backends is a Collector config change, not an app code change.

**In the MyApp kit:** the host csprojs reference the SDK packages; the Collector runs in Docker; backends are Grafana-stack (Prometheus/Loki/Tempo). If you later add Honeycomb for a specific service, you add one `otlp/honeycomb` exporter in the Collector gateway — zero app changes.

### B.3 The three signals — when to use each

```
   ┌─────────────────────────────────────────────────────────────┐
   │  METRICS                                                    │
   │  Aggregated, numeric, always-on, cheap.                     │
   │  Good for: dashboards, SLOs, alerts, capacity planning.     │
   │  Bad for: "what happened in THIS request?"                  │
   │  MyApp examples:                                            │
   │    • http.server.request.duration (per route)               │
   │    • myapp.catalog.products_created (counter)               │
   │    • process.runtime.dotnet.gc.pause.time                   │
   └─────────────────────────────────────────────────────────────┘

   ┌─────────────────────────────────────────────────────────────┐
   │  TRACES                                                     │
   │  Per-request tree of spans. Expensive (one span per call).  │
   │  Good for: "why was this slow?", causality, fan-out.        │
   │  Bad for: aggregate trends (use metrics).                   │
   │  MyApp examples:                                            │
   │    • POST /products → Product.Create → SaveChanges          │
   │    •  → PublishProductCreated → Wolverine → RabbitMQ        │
   │    •  → [link] OrdersApi consumer → ProjectProductCatalog   │
   └─────────────────────────────────────────────────────────────┘

   ┌─────────────────────────────────────────────────────────────┐
   │  LOGS                                                       │
   │  Structured text, always-on, indexed by trace_id.           │
   │  Good for: error messages, business events, debug detail.   │
   │  Bad for: numeric trends (use metrics), causality (traces). │
   │  MyApp examples:                                            │
   │    • "Product {ProductId} created"                           │
   │    • "Auth failed for {Email}"                               │
   │    • Wolverine handler started / completed                   │
   └─────────────────────────────────────────────────────────────┘
```

The three signals **correlate** through `trace_id` — the same 32-char hex appears in a Tempo span, a Loki log line, and (via exemplars) a Prometheus metric sample. That correlation is what makes observability more than monitoring.

### B.4 What is a trace?

A **trace** is a tree of **spans**. Each span represents one unit of work: an HTTP request, a DB query, a message publish, a domain event handler. The root span is the entry point (e.g. `POST /products`); child spans are nested work.

Wolverine does **not** use a span link for RabbitMQ consumption. It starts the consumer activity with the producer's parent id, so the consumer is a child in the same trace. Span links are for sibling work that cannot share a parent chain; create them manually only for transports that cannot propagate context.

```
POST /products                                    ← root span (ASP.NET Core)
└── Product.Create                                ← child (custom ActivitySource)
    └── dbContext.Products.Add                    ← child (EF Core)
        └── SaveChanges                           ← child (EF Core)
            └── PublishProductCreatedIntegration  ← child (Wolverine publisher)
                                                    ╲
                                                     ╲ [span link]
                                                      ╲
                  OrdersApi.Receive ProductCreatedIntegrationEvent  ← root of NEW trace (linked, not child)
                  └── ProjectProductCatalog handler
```

The structure matters: parent/child means "the child happened *because of* the parent" (causal). Span links mean "these two traces are *related* but one didn't cause the other synchronously" (async fan-out). §10.1 of `TELEMETRY.md` explains why Wolverine uses links across RabbitMQ.

### B.5 What is a metric?

A **metric** is a numeric time series identified by:
- a **name** (`http.server.request.duration`)
- a set of **labels** (`http_route=POST /products`, `http_response_status_code=201`, `service_name=myapp.apihost`)
- a **data point** (counter increment, histogram bucket, gauge value)

Unlike traces (one per request) or logs (one per event), metrics are **pre-aggregated** in the app: 1000 identical requests produce one counter value of 1000, not 1000 separate records. That's why metrics are cheap and always-on.

OTel defines six instrument types (see Part C §5 for all of them); the three you'll use most:
- **Counter** — only goes up (requests received, products created)
- **Histogram** — distribution of values (request duration, payload size)
- **Gauge** — current value, goes up and down (active requests, DLQ depth)

### B.6 What is OTLP?

**OTLP** (OpenTelemetry Protocol) is the wire format OTel uses between app → Collector and Collector → backend. It runs over **gRPC** (port 4317) or **HTTP** (port 4318). It carries all three signals (traces, metrics, logs) plus resource attributes in one Protobuf-encoded stream.

```
.NET app  ──gRPC :4317──►  Collector  ──gRPC :4317──►  Tempo (traces)
                           Collector  ──HTTP :3100──►  Loki  (logs, via OTLP HTTP)
                           Collector  ──scrape────►   Prometheus (metrics, via collector's prometheus exporter)
```

The .NET side calls `AddOtlpExporter()`; you configure the endpoint once (`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317`). Everything else is the Collector's job.

**Why OTLP over vendor-specific protocols?** Because Prometheus' scrape model doesn't work for traces/logs (those are push-based), and Jaeger's Thrift protocol is Jaeger-only. OTLP is the one protocol that carries all three signals to any backend.

### B.7 What is a log? Why logs for observability?

A **log** is a timestamped structured record emitted by `Microsoft.Extensions.Logging`. In the OTel world, logs are the **third signal** — not just text dumped to stdout, but structured records carrying:
- the **trace_id** and **span_id** of the active Activity (auto-attached by the OTel logs SDK)
- **resource attributes** (`service.name`, `deployment.environment`)
- **scopes** (the `ILogger<T>` category + any `BeginScope` values)
- a **severity** level, a **message template**, and the formatted message

**Why logs (when you have traces)?** Traces answer "what happened structurally?" (which calls were made, how long). Logs answer "what did the code decide?" (business logic, validation outcomes, error reasons). Example:

```
Trace says:    POST /products → 400 Bad Request (3ms)
Log says:      "Validation failed: PRODUCT.NAME — Name must be between 3 and 100 characters."
```

The trace tells you the request failed fast; the log tells you *why*. With `trace_id` linking them, you click from the trace to the exact log line.

**Why logs (when you have metrics)?** Metrics tell you "how often does validation fail?" Logs tell you "what was the actual input that failed?" You need both.

---

## Part C — .NET terminology & instrument types

### C.1 .NET → OpenTelemetry mapping

.NET had its own diagnostics APIs long before OTel. OTel reuses them, so the names don't always match:

| OpenTelemetry spec term | .NET type | Notes |
|---|---|---|
| **Tracer** | `System.Diagnostics.ActivitySource` | Create one per module (`new ActivitySource("MyApp.Catalog")`) |
| **Span** | `System.Diagnostics.Activity` | What `ActivitySource.StartActivity()` returns |
| **Meter** | `System.Diagnostics.Metrics.Meter` | Same name — direct mapping |
| **Counter** | `Counter<T>` | From `Meter.CreateCounter<T>()` |
| **Histogram** | `Histogram<T>` | From `Meter.CreateHistogram<T>()` |
| **Gauge** | `ObservableGauge<T>` | From `Meter.CreateObservableGauge<T>()` — callback-based |
| **UpDownCounter** | `UpDownCounter<T>` | From `Meter.CreateUpDownCounter<T>()` |
| **Logger** | `Microsoft.Extensions.Logging.ILogger` | OTel logs SDK wraps it |
| **LogRecord** | (internal) | The OTel SDK converts `ILogger` calls to `LogRecord` |
| **Resource** | `ResourceBuilder` | Built via `ConfigureResource(r => r.AddService(...))` |
| **Context propagation** | `System.Diagnostics.Activity.Current` | W3C traceparent flows automatically |
| **Baggage** | `System.Diagnostics.Baggage.Current` | See Part D |

**The key insight:** you don't write OTel-specific code. You write `ActivitySource.StartActivity("...")` and `meter.CreateCounter<long>("...")` — standard .NET APIs. The OTel SDK (registered via `AddOpenTelemetry()`) is what makes those calls produce exported telemetry.

### C.2 What is a Resource?

A **Resource** is the identity of the telemetry producer. It's a set of key-value attributes attached to **every** span, metric, and log record emitted by a process. It answers: *who emitted this?*

```csharp
otel.ConfigureResource(r => r
    .AddService(
        serviceName: "myapp.apihost",        // REQUIRED — primary identifier
        serviceVersion: "1.2.3",             // helps correlate deploys to telemetry changes
        serviceInstanceId: Environment.MachineName)  // distinguishes replicas
    .AddAttributes(new Dictionary<string, object>
    {
        ["deployment.environment"] = "prod",           // dev / staging / prod
        ["host.name"] = Environment.MachineName,
        ["process.runtime.name"] = "dotnet",
        ["process.runtime.version"] = Environment.Version.ToString(),
    }));
```

**Why it matters:** Resource attributes become labels in Prometheus, stream labels in Loki, and trace tags in Tempo. Without `service.name`, every metric series collapses into one undifferentiated blob and every log lands in a single stream. With it, you can filter `http_server_request_duration{service_name="myapp.apihost"}` or `{service_name="myapp.ordersapi"}`.

**Architecture rule:** `service.name` is the only mandatory attribute. Pick a stable scheme (the kit uses `myapp.<host>`: `myapp.apihost`, `myapp.ordersapi`, etc.) and never change it post-deploy — changing it creates a new time series and breaks historical dashboards.

### C.3 What are instrumentation libraries?

**Instrumentation libraries** are NuGet packages that auto-instrument framework calls so you don't have to write `StartActivity` around every `HttpContext` or `SqlCommand`. They hook into framework extension points and emit spans/metrics for you.

| Library | What it captures | Hook |
|---|---|---|
| `OpenTelemetry.Instrumentation.AspNetCore` | HTTP server spans + metrics (`http.server.request.duration`, `http.server.active_requests`) | ASP.NET Core middleware / endpoint routing events |
| `OpenTelemetry.Instrumentation.Http` | Outbound HttpClient spans (`http.client.request.duration`) | `HttpClientHandler` diagnostic listener |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | DB spans (`db.system`, `db.operation`, `db.statement` if enabled) | EF Core `DiagnosticListener` |
| `OpenTelemetry.Instrumentation.Runtime` | .NET runtime metrics (GC, ThreadPool, Kestrel, memory) | `EventSource` subscriptions |
| `OpenTelemetry.Instrumentation.Process` | Process CPU + memory | `/proc` or OS APIs |
| `OpenTelemetry.Instrumentation.StackExchangeRedis` | Redis command spans | `ConnectionMultiplexer` internal events |
| Wolverine (built in) | Send/receive/process spans + metrics | `AddSource("Wolverine")`, `AddMeter("Wolverine*")` |

**How they work:** each subscribes to a .NET `DiagnosticListener` or `EventSource` that the framework already emits. When ASP.NET Core raises its `Microsoft.AspNetCore.Hosting.HttpRequestIn.Start` event, the AspNetCore instrumentation creates an `Activity` for it — automatically, no code in your handler.

**Architecture rule:** enable instrumentation libraries at the **host** level (in `AddMyAppObservability`), never at the module level. Modules shouldn't know about tracing — they just use `HttpClient` / `DbContext` / Wolverine, and the host's instrumentation wraps those calls.

### C.4 Exporters — Jaeger vs Tempo vs OTLP

The course teaches "exporting to Jaeger" because Jaeger was historically the default trace UI. In the Grafana stack we use **Tempo**. The difference:

| | Jaeger (legacy) | Tempo (current) |
|---|---|---|
| Ingest protocol | Jaeger Thrift / gRPC | **OTLP** |
| Storage | Cassandra / Elasticsearch | Object storage (S3 / local) |
| UI | Jaeger UI (standalone) | Grafana (native datasource) |
| Cardinality | Indexes service + tags → expensive | Does NOT index tags → cheap, uses TraceQL at query time |
| In the kit | Mentioned as `:14268` legacy port (TM §5.5) | Primary trace store |

**The OTLP exporter in .NET is backend-agnostic:**

```csharp
.AddOtlpExporter(o =>
{
    o.Endpoint = new Uri("http://otel-collector:4317");
    o.Protocol = OtlpExportProtocol.Grpc;
});
```

The .NET app doesn't know whether the Collector forwards to Jaeger, Tempo, or both. You can run both side-by-side by adding two exporters in the Collector gateway:

```yaml
exporters:
  otlp/tempo: { endpoint: tempo:4317 }
  otlp/jaeger: { endpoint: jaeger:4317 }   # if you also deploy Jaeger
service:
  pipelines:
    traces:
      receivers: [otlp]
      exporters: [otlp/tempo, otlp/jaeger]
```

For the MyApp kit, **Tempo only** is recommended — Grafana native correlation, no extra service.

### C.5 The six instrument types

OTel defines six metric instruments. The course covers Counter, Histogram, Gauge; the kit uses three of them. Here's the full set with architecture examples:

| Instrument | Goes up? | Goes down? | Aggregation | When to use | MyApp example |
|---|---|---|---|---|---|
| **Counter<T>** | ✓ | ✗ | Sum | "How many X happened?" | `myapp.catalog.products_created` — total products ever created |
| **UpDownCounter<T>** | ✓ | ✓ | Sum | "How many X are *currently* in state Y?" | `myapp.orders.active` — orders currently in `Pending` state (goes up on create, down on ship) |
| **Histogram<T>** | n/a | n/a | Bucket distribution | "What's the distribution of X?" | `http.server.request.duration` — request latency p50/p95/p99 |
| **ObservableCounter<T>** | ✓ (via callback) | ✗ | Sum | Counter where you read the value from an external source | `process.runtime.dotnet.gc.collections_count` — GC reads its own counter |
| **ObservableGauge<T>** | ✓ | ✓ (via callback) | Last value | Gauge where you read from an external source | `process.runtime.dotnet.thread_pool.queue.length` — ThreadPool reports current value |
| **Gauge<T>** *(API only, no SDK support in .NET yet)* | ✓ | ✓ | Last value | Non-callback gauge | Use `ObservableGauge` instead in .NET |

**The two the kit is missing and should add:**

#### UpDownCounter — for OrdersApi

The Orders module tracks orders moving through statuses (Pending → Paid → Shipped). A Counter can only go up; you'd compute "current pending" as `created - shipped`, which is fragile. An **UpDownCounter** tracks it directly:

```csharp
// src/Services/MyApp.OrdersApi/Orders/OrderMetrics.cs
public static class OrderMetrics
{
    public static readonly Meter Meter = new("MyApp.Orders", "1.0.0");

    // Increments on order created, decrements when order leaves Pending status
    public static readonly UpDownCounter<int> ActiveOrders =
        Meter.CreateUpDownCounter<int>("myapp.orders.active");

    public static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("myapp.orders.created");
}

// In PlaceOrder handler:
OrderMetrics.OrdersCreated.Add(1, new("status", "pending"));
OrderMetrics.ActiveOrders.Add(1, new("status", "pending"));

// In ShipOrder handler:
OrderMetrics.ActiveOrders.Add(-1, new("status", "pending"));
OrderMetrics.ActiveOrders.Add(1, new("status", "shipped"));
```

Prometheus sees `myapp_orders_active{status="pending"}` as a gauge — the current count at scrape time. No subtraction needed.

#### ObservableGauge — for the Wolverine DLQ

Wolverine exposes `wolverine.dead_letter_queue.size` as a gauge via its own instrumentation. If you want a custom one (e.g. "current Elastic index document count"):

```csharp
public static readonly ObservableGauge<long> SearchIndexSize =
    Meter.CreateObservableGauge<long>(
        "myapp.search.index_size",
        () => MeasureIndexSize(),    // async callback, called on every scrape
        unit: "items",
        description: "Current document count in the products search index");
```

Register `AddMeter("MyApp.Search")` in `AddMyAppObservability` and Prometheus scrapes the callback value every 15s.

---

## Part D — Context propagation: Baggage, propagators, risks

This is the biggest gap in the existing docs. Part D fills it.

### D.1 What is Baggage?

**Baggage** is a cross-process key-value store that propagates alongside the trace context. While `traceparent` carries *which trace this is*, baggage carries *arbitrary business context* — user ID, tenant ID, feature flags, request origin — without you having to add a parameter to every method or a column to every log.

```
ApiHost (HTTP request)
  │  Activity.Current = { trace_id: abc, span_id: def }
  │  Baggage.Current    = { user.id: 6515..., tenant.id: acme, feature.new_pricing: true }
  │
  │  Wolverine.PublishAsync(orderPlacedEvent)
  │     ↓ Wolverine serializes Baggage into RabbitMQ headers
  │
  ▼
RabbitMQ message headers:
    traceparent: 00-abc..-def..-01
    baggage: user.id=6515...,tenant.id=acme,feature.new_pricing=true
  │
  ▼
OrdersApi consumer
  │  Activity.Current = { trace_id: abc (linked), span_id: ghi }
  │  Baggage.Current    = { user.id: 6515..., tenant.id: acme, feature.new_pricing: true }  ← restored!
  │
  └── Logger.LogInformation("Processing order for tenant {TenantId}", Baggage.Current.GetBaggage("tenant.id"))
      → log line carries tenant.id without any method parameter
```

**Why not just use span attributes?** Span attributes are read-only after the span ends, and they only exist on traces (which are sampled). Baggage propagates to **every** signal — logs, metrics, downstream HTTP calls — and you can read it synchronously in any handler without an `Activity` reference.

**Why not just add parameters?** Because in the MyApp kit, a Wolverine consumer has no compile-time knowledge of the ApiHost's JWT. Baggage carries that context across the RabbitMQ boundary without the consumer needing a new method signature or a DB lookup.

### D.2 Adding Baggage — the MyApp pattern

**Use case:** propagate `user.id` and `tenant.id` from the ApiHost JWT through Wolverine/RabbitMQ to OrdersApi, InvoicesApi, ShippingApi consumers. Every consumer's logs and spans then carry the user/tenant without a DB roundtrip.

#### Step 1 — set baggage in the HTTP pipeline

Add a middleware that reads the JWT claims and sets baggage for the request:

```csharp
// src/Shared/MyApp.Observability/UserContextBaggageMiddleware.cs
using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace MyApp.Observability;

/// <summary>
/// Copies user.id + tenant.id from JWT claims into Baggage so they
/// propagate to every downstream HTTP call, every Wolverine message,
/// and every log line in this request.
/// </summary>
public sealed class UserContextBaggageMiddleware
{
    private readonly RequestDelegate _next;

    public UserContextBaggageMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            var baggageItems = new List<KeyValuePair<string, string>>
            {
                new("user.id", userId),
            };

            var tenantId = context.User.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrEmpty(tenantId))
                baggageItems.Add(new("tenant.id", tenantId));

            // SetBaggage replaces the current baggage — call once at the start
            Baggage.Current = Baggage.Current.SetBaggage(baggageItems);
        }

        await _next(context);
    }
}
```

Wire it in the host's `Presentation/WebApplicationExtensions.cs`:

```csharp
public static WebApplication UseSharedPipeline(this WebApplication app)
{
    app.UseSharedExceptionHandling();
    app.UseForwardedHeaders();
    app.UseCors(/* ... */);
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseAuthorization();

    // NEW — after auth so User is populated
    app.UseMiddleware<UserContextBaggageMiddleware>();

    app.MapHealthChecks("/health").AllowAnonymous();
    app.MapOpenApi().AllowAnonymous();
    return app;
}
```

#### Step 2 — read baggage in a handler or consumer

```csharp
// src/Services/MyApp.OrdersApi/Consumers/ProjectProductCatalog/ProductCreatedIntegrationEventHandler.cs
public sealed class ProductCreatedIntegrationEventHandler
{
    public async Task Handle(
        ProductCreatedIntegrationEvent message,
        ServiceADbContext db,
        ILogger<ProductCreatedIntegrationEventHandler> logger,
        CancellationToken ct)
    {
        // Baggage was propagated through RabbitMQ headers by Wolverine — restored here
        var userId = Baggage.Current.GetBaggage("user.id") ?? "unknown";
        var tenantId = Baggage.Current.GetBaggage("tenant.id") ?? "default";

        logger.LogInformation(
            "Indexing product {ProductId} for tenant {TenantId} (triggered by user {UserId})",
            message.ProductId, tenantId, userId);

        // ... handler body
    }
}
```

The log line in Loki now carries `tenant.id` and `user.id` as structured fields — filterable without joining to the Users module's DB.

#### Step 3 — propagate baggage automatically through Wolverine

Wolverine has OpenTelemetry built in. Register `.AddSource("Wolverine")` and `.AddMeter("Wolverine*")`; there is no `WolverineFx.OpenTelemetry` package and no options flag.

Wolverine propagates `traceparent` and `tracestate` through RabbitMQ headers. Baggage propagation is version- and transport-dependent: verify it for your Wolverine version before relying on `user.id` in a consumer. If it is not propagated, carry the identifier in the message contract.

Verify it works by checking the RabbitMQ message headers in the management UI (`http://localhost:15672`):

```
Headers:
  traceparent: 00-abc123...-def456...-01
  baggage:     user.id=6515...,tenant.id=acme
  wolverine.message_id: ...
```

#### Step 4 — propagate baggage through HttpClient

For direct HTTP calls (Gateway → ApiHost, or ApiHost → external API), the OTel HttpClient instrumentation propagates baggage automatically — no code needed. The `W3C Baggage` header is added to every outbound request.

### D.3 Custom Context Propagator — when + how

**When you need one:** the default OTel propagator uses W3C `traceparent` + `baggage` headers. You need a custom propagator when:

1. **You integrate with a non-W3C system** — e.g. an AWS service that expects `X-Amzn-Trace-Id`, or a legacy service that reads `X-Correlation-Id`.
2. **You need to propagate through a transport that doesn't preserve arbitrary headers** — e.g. some message brokers strip unknown headers.
3. **You want to inject correlation IDs into a system that doesn't speak OTel at all** — e.g. writing a `correlation-id` header for a downstream PHP service.

**Architecture use case:** the Gateway (YARP) forwards requests to ApiHost. Some legacy clients send `X-Correlation-Id` instead of `traceparent`. You want to honor both.

```csharp
// src/Shared/MyApp.Observability/CompositePropagator.cs
using OpenTelemetry.Context.Propagation;
using System.Diagnostics;
using System.Text;

namespace MyApp.Observability;

/// <summary>
/// Propagator that reads/writes BOTH W3C traceparent AND a legacy
/// X-Correlation-Id header. On extract, if traceparent is missing but
/// X-Correlation-Id is present, it adopts the legacy ID as the trace ID.
/// </summary>
public sealed class LegacyCorrelationPropagator : TextMapPropagator
{
    private readonly TextMapPropagator _inner = new TraceContextPropagator();
    public const string CorrelationIdHeader = "X-Correlation-Id";

    // Inject: write traceparent (inner propagator) + X-Correlation-Id (trace_id)
    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        _inner.Inject(context, carrier, setter);
        // Also write the trace ID as X-Correlation-Id for legacy consumers
        var traceId = context.ActivityContext.TraceId.ToHexString();
        setter(carrier, CorrelationIdHeader, traceId);
    }

    // Extract: try traceparent first; fall back to X-Correlation-Id
    public override PropagationContext Extract<T>(PropagationContext context, T carrier, Func<T, string, IEnumerable<string>> getter)
    {
        var extracted = _inner.Extract(context, carrier, getter);
        if (extracted.ActivityContext.TraceId != default)
            return extracted;   // W3C header present — use it

        // Fall back to legacy header
        var corrIds = getter(carrier, CorrelationIdHeader);
        var corrId = corrIds?.FirstOrDefault();
        if (!string.IsNullOrEmpty(corrId) && ActivityTraceId.TryParseFromString(corrId, out var traceId))
        {
            return new PropagationContext(
                new ActivityContext(traceId, default, ActivityTraceFlags.None, isRemote: true),
                extracted.Baggage);
        }
        return context;
    }

    public override IEnumerable<string> Fields => _inner.Fields.Append(CorrelationIdHeader);
}
```

Register it at startup, replacing the default:

```csharp
// In AddMyAppObservability, before AddOpenTelemetry():
var composite = new CompositeTextMapPropagator(new TextMapPropagator[]
{
    new LegacyCorrelationPropagator(),
    new BaggagePropagator(),
});
// Propagators.DefaultTextMapPropagator is read-only in OpenTelemetry 1.17.
// To use a custom propagator, build the provider through Sdk.CreateTracerProviderBuilder()
// or set OTEL_PROPAGATORS for a built-in one.
```

Now every HttpClient call and Wolverine message carries both `traceparent` (W3C) and `X-Correlation-Id` (legacy). Legacy consumers that only read the latter still get a correlation ID.

### D.4 Propagation risks

| Risk | What goes wrong | Mitigation |
|---|---|---|
| **PII in baggage** | You put `user.email` in baggage; it propagates to every downstream service's logs and to RabbitMQ message headers (visible in the management UI) | Never put PII in baggage. Use opaque IDs (`user.id`, `tenant.id`) that require a DB lookup to resolve. The kit's JWT uses `sub` (user GUID) — safe. |
| **Cardinality explosion in metrics** | You tag a metric with `user.id` (10k users) → 10k time series → Prometheus OOM | Baggage is for **traces + logs**, not metric labels. OTel metrics don't auto-pick up baggage; you must explicitly add it. Don't add `user.id` to metric attributes. |
| **Unbounded baggage size** | Each hop adds more keys; after 10 hops the headers are 2KB | W3C limits baggage to ~8KB total. Keep keys short (`uid` not `user_identifier`), values opaque (GUIDs not JSON). |
| **Propagating through untrusted boundaries** | You receive an HTTP request with a forged `traceparent` claiming to be part of an existing trace | OTel's traceparent includes a `flags` byte. For external-facing endpoints, accept the `trace_id` but always generate a new `span_id` and start a new root span — don't accept the parent span as your caller. |
| **Baggage leaking across requests** | ASP.NET Core reuses threads; `Baggage.Current` is `AsyncLocal` but you must reset it per request | The middleware in D.2 sets baggage with `SetBaggage` which returns a new `Baggage` object — `Baggage.Current = ...` resets the AsyncLocal for this request only. |
| **Wolverine consumer sees stale baggage** | A message sits in RabbitMQ for 2h; the user has since been banned | Baggage is a snapshot at publish time. Always re-validate auth-sensitive claims (banned status, permission version) in the consumer — don't trust baggage as an auth check. |

**Architecture rule for the MyApp kit:**
- ✓ Put in baggage: `user.id`, `tenant.id`, `request.id`, `feature.flag.<name>`
- ✗ Never put in baggage: `user.email`, `user.name`, JWT tokens, passwords, PII, large JSON blobs
- ✓ Use baggage in: traces, logs, Wolverine consumers
- ✗ Never use baggage in: metric attributes (cardinality), auth decisions (staleness)

---

## Part E — Deep diving into tracing

### E.1 Custom Span Processor

A **span processor** is a hook in the SDK's span export pipeline. It runs after a span ends but before it's sent to the exporter. Use cases: redact PII, drop noisy spans, add computed attributes, batch differently per signal.

**Architecture use case:** redact email addresses and JWT tokens from span attributes before they reach the collector. The kit's auth module logs email everywhere; you don't want PII in Tempo.

```csharp
// src/Shared/MyApp.Observability/PiiRedactionProcessor.cs
using OpenTelemetry;
using System.Text.RegularExpressions;

namespace MyApp.Observability;

/// <summary>
/// Span processor that runs before export and redacts values matching
/// email or JWT patterns from all span attributes. Runs on every span
/// in every host — register once per tracer provider.
/// </summary>
public sealed class PiiRedactionProcessor : BaseProcessor<Activity>
{
    // JWT pattern: three base64 segments separated by dots
    private static readonly Regex JwtPattern =
        new(@"^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    // Email pattern
    private static readonly Regex EmailPattern =
        new(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled);

    public override void OnEnd(Activity activity)
    {
        // Redact tags (attributes)
        for (int i = 0; i < activity.TagObjects.Count; i++)
        {
            var (key, value) = activity.TagObjects[i];
            if (value is string s)
            {
                var redacted = Redact(s);
                if (redacted != s)
                {
                    activity.SetTag(key, redacted);
                }
            }
        }
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // JWT — replace entirely
        if (JwtPattern.IsMatch(value))
            return "<redacted:jwt>";

        // Email — mask the local part
        return EmailPattern.Replace(value, m =>
            $"{m.Value[..Math.Min(2, m.Value.IndexOf('@'))]}***@{m.Value[(m.Value.IndexOf('@') + 1)..]}");
    }
}
```

Register it in `AddMyAppObservability`:

```csharp
otel.WithTracing(t => t
    .AddSource("Wolverine")
    .AddSource("MyApp.Catalog")
    .AddSource("MyApp.Orders")
    .AddSource("MyApp.Search")
    .AddSource("MyApp.Invoices")
    .AddSource("MyApp.Shipping")
    .AddProcessor(new PiiRedactionProcessor())   // ← runs on every span before export
    .AddAspNetCoreInstrumentation()
    // ... rest
    .AddOtlpExporter());
```

**How processors differ from exporters:** a processor runs **per-span** in the SDK; an exporter runs **per-batch**. Use processors for per-span mutation (redaction, enrichment, filtering); use exporters for transport (OTLP, console).

### E.2 Span Events

A **span event** is a timestamped annotation on a span — like a mini-log that lives *inside* the trace, not in Loki. Events have a name, a timestamp, and optional attributes. They show up in Tempo as markers on the span's timeline.

**When to use events vs attributes vs logs:**

| | Span attribute | Span event | Log |
|---|---|---|---|
| When set | At span creation / during work | At a specific moment in the span | Anytime |
| What it represents | A property of the span ("http.route=POST /products") | A point-in-time occurrence ("validation failed at 14:32:01") | A message from the code |
| Where it shows | Span detail panel | Span timeline (marker) | Loki |
| Indexed? | Yes (Tempo tag) | No (visible in span view only) | Yes (Loki label/field) |

**Architecture use case:** emit a span event when each domain event is raised. The `Product.Create` method raises `ProductCreatedDomainEvent`; add an event so you can see *when in the span's lifetime* the domain event fired.

```csharp
// src/Modules/Catalog/Products/Product.cs (excerpt)
public static ErrorOr<Product> Create(string name, decimal price, int stock, Category category, Brand brand)
{
    // ... validation ...

    var product = new Product { /* ... */ };

    var createdEvent = new ProductCreatedDomainEvent(product.Id, product.Name, product.Price);
    product.AddDomainEvent(createdEvent);

    // Emit a span event so the trace timeline shows when the domain event was raised
    var activity = Activity.Current;
    activity?.AddEvent(new ActivityEvent("domain.event.raised", DateTimeOffset.UtcNow, new ActivityTagsCollection
    {
        new("event.type", "ProductCreated"),
        new("event.product_id", product.Id),
        new("event.product_name", product.Name),
    }));

    return product;
}
```

In Tempo, the `Product.Create` span now shows a marker labeled `domain.event.raised` at the moment the event was added. Click the marker → see the attributes. This is invaluable for understanding the sequence of events inside a long span.

**Another use case — record exceptions as events:**

```csharp
try
{
    await dbContext.SaveChangesAsync(ct);
}
catch (DbUpdateException ex)
{
    // RecordException adds an event named "exception" with standard
    // attributes: exception.type, exception.message, exception.stacktrace
    Activity.Current?.RecordException(ex);
    throw;
}
```

`RecordException` is a built-in helper that adds an event following the OTel semantic convention for exceptions. Tempo renders it as an error marker on the span.

### E.3 Representing Errors and Exceptions

A span has a **status**: `Unset`, `Ok`, or `Error`. The instrumentation libraries set this automatically for HTTP 5xx and DB failures, but for **business errors** (the kit's `ErrorOr<T>` returning `Error.NotFound(...)`), you must set it yourself.

**Architecture use case:** a pipeline behavior that maps `ErrorOr<T>` results to span status. Every command handler returns `ErrorOr<T>`; if the result is an error, set the current span's status to `Error` and add the error code as an attribute. This makes business failures show up as red spans in Tempo.

```csharp
// src/Shared/Application/Behaviors/ErrorTracingBehavior.cs
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Shared.Application.Behaviors;

/// <summary>
/// Pipeline behavior that records ErrorOr results on the active span.
/// Register after TransactionBehavior so it runs after the handler returns.
/// </summary>
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

        // ErrorOr<T> is Success<T> | Error; check if it's an error
        if (response is IErrorOr { IsError: true })
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "ErrorOr returned errors");
        }
        else
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Ok);
        }

        return response;
    }
}

// IErrorOr already exists in Shared.Common.Errors and exposes IsError.
// Add `IReadOnlyList<Error> Errors` to that interface if you need error codes;
// ErrorOr<T> already implements it.
```

Register it in the host's `Application/DependencyInjection.cs`:

```csharp
services.AddPipelineBehavior(typeof(ErrorTracingBehavior<,>));
```

Now every command handler that returns `Error.NotFound("PRODUCT.NOT_FOUND", ...)` produces a red span in Tempo with the error code as an attribute. You can filter Tempo:

```traceql
{ status = error && span."error.code" = "PRODUCT.NOT_FOUND" }
```

### E.4 Span Links — manual creation

Wolverine uses a parent/child relationship across RabbitMQ (the consumer activity is started with the producer's parent id). The manual code below is for a **non-Wolverine** transport or a worker that intentionally starts a new trace. It is not the pattern for normal Wolverine consumers.

**Architecture use case:** the Search module's `ProductCreatedIntegrationEventHandler` runs after the ApiHost publishes `ProductCreatedIntegrationEvent`. If you weren't using Wolverine (e.g. raw RabbitMQ client), you'd create the link manually:

```csharp
// Hypothetical: raw RabbitMQ consumer (not Wolverine) — manual span link
public sealed class RawRabbitMqConsumer
{
    private readonly ActivitySource _activitySource;
    private readonly ElasticsearchClient _es;

    public RawRabbitMqConsumer(ActivitySource activitySource, ElasticsearchClient es)
    {
        _activitySource = activitySource;
        _es = es;
    }

    public async Task ConsumeAsync(BasicDeliverEventArgs ea, CancellationToken ct)
    {
        // 1. Extract the producer's trace context from RabbitMQ headers
        var propagator = Propagators.DefaultTextMapPropagator;
        var parentContext = propagator.Extract(default, ea.BasicProperties.Headers,
            (headers, name) =>
                headers.TryGetValue(name, out var bytes)
                    ? new[] { Encoding.UTF8.GetString((byte[])bytes) }
                    : Array.Empty<string>());

        // 2. Create a NEW root span (not a child) linked to the producer's context
        var link = new ActivityLink(parentContext.ActivityContext);
        using var activity = _activitySource.StartActivity(
            "consume.ProductCreated",
            ActivityKind.Consumer,
            new ActivityContext(default, default, default),  // start a new root, not parentContext
            links: new[] { link });

        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.destination", ea.Exchange);
        activity?.SetTag("messaging.message_id", ea.BasicProperties.MessageId);

        // 3. Decode the message body
        var message = JsonSerializer.Deserialize<ProductCreatedIntegrationEvent>(ea.Body.Span);

        // 4. Do the work
        await _es.IndexAsync(new ProductSearchDocument
        {
            ProductId = message.ProductId,
            Name = message.Name,
        }, ct);

        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}
```

The key line is `links: new[] { link }` — that creates a span link instead of a parent relationship. Use it only when the consumer must start a new trace; normal Wolverine consumers should stay in the producer's trace.

**When to use links vs parent/child:**

| Relationship | Use | Why |
|---|---|---|
| Synchronous call (HTTP → handler → DB) | **Parent/child** | The child happened *because of* the parent, synchronously |
| Async fan-out (publish → queue → consume later) | **Span link** | The consumer happened *because of* the publish, but at a different time, possibly on a different host. Treating it as parent/child would inflate the producer's span duration with the consumer's latency. |
| Batch processing (one trigger → N parallel workers) | **Span links** | Each worker links back to the trigger; they're siblings, not children of each other |

### E.5 Custom Span Processor — filtering

Another processor pattern: **drop spans you don't want to export**. The collector can do this (TM §6.2 `filter/drop_healthchecks`), but doing it in the SDK saves the network round-trip.

```csharp
// src/Shared/MyApp.Observability/NoisySpanFilterProcessor.cs
public sealed class NoisySpanFilterProcessor : BaseProcessor<Activity>
{
    private static readonly HashSet<string> DroppedRoutes = new()
    {
        "/health",
        "/alive",
        "/openapi/v1.json",
    };

    public override void OnStart(Activity activity)
    {
        // Check if this is a span we want to drop
        if (activity.OperationName == "Microsoft.AspNetCore.Hosting.HttpRequestIn")
        {
            var route = activity.GetTagItem("http.target") as string;
            if (route is not null && DroppedRoutes.Any(r => route.StartsWith(r)))
            {
                // Mark to skip on end
                activity.SetTag("_skip_export", true);
            }
        }
    }

    public override void OnEnd(Activity activity)
    {
        // If marked to skip, we can't actually prevent export from a processor
        // (processors can't drop spans — only modify them). To DROP spans,
        // use a Sampler (Part F) or the collector filter processor.
        // This processor pattern is for ENRICHMENT, not filtering.
    }
}
```

**Important distinction:** processors can **modify** spans but cannot **drop** them. To drop spans in the SDK, use a **Sampler** (Part F). To drop spans at the pipeline level, use the collector's `filter` processor (TM §6.2).

---

## Part F — Sampling

### F.1 What is sampling and why?

**Sampling** is the decision of *which traces to export*. You can't export every trace at high traffic — 1000 req/s × 20 spans/req = 20k spans/s, which overwhelms the collector and Tempo. Sampling keeps the volume manageable while preserving enough traces to debug real issues.

Two flavours:

| | Head sampling | Tail sampling |
|---|---|---|
| **Where** | At span creation (SDK) | After the trace completes (collector) |
| **Decides** | Per-span, before any attributes exist | Per-trace, with all attributes + status visible |
| **Can keep all errors?** | No — decision made before error happens | Yes — can sample 100% of error traces |
| **Can keep all slow traces?** | No — decision made before duration known | Yes — can sample 100% of traces > 500ms |
| **Volume reduction** | Aggressive (drops at source) | Smart (keeps interesting, drops boring) |
| **In the kit** | Not used (TM uses OTel default `ParentBased(AlwaysOn)`) | Used at collector gateway (TM §6.2 `tail_sampling`) |

### F.2 Head sampling — the default + custom

**The OTel .NET default** is `ParentBased(AlwaysOn)` — every trace is sampled unless a remote parent said "don't sample". In practice this means "sample everything". At high traffic you need to lower this.

**Built-in samplers:**

| Sampler | What it does |
|---|---|
| `AlwaysOnSampler` | Sample 100% |
| `AlwaysOffSampler` | Sample 0% (for testing exporter setup) |
| `TraceIdRatioBasedSampler(0.1)` | Sample 10% randomly, by trace ID |
| `ParentBasedSampler(inner)` | Inherit the parent's sampling decision — use as a wrapper |

**Architecture use case:** sample 10% of read traffic but 100% of write traffic. Writes (CreateProduct, UpdateProduct, DeleteProduct) are business-critical and low-volume; reads (GetProducts, GetProduct) are high-volume and less interesting per-request.

```csharp
// src/Shared/MyApp.Observability/RouteAwareSampler.cs
using OpenTelemetry.Trace;

namespace MyApp.Observability;

/// <summary>
/// Head sampler that always samples write operations (POST/PUT/DELETE)
/// and samples reads (GET) at 10%. Falls back to parent-based for non-HTTP spans.
/// </summary>
public sealed class RouteAwareSampler : Sampler
{
    private readonly Sampler _readSampler = new TraceIdRatioBasedSampler(0.1);
    private readonly Sampler _writeSampler = new AlwaysOnSampler();
    private readonly Sampler _defaultSampler = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters parameters)
    {
        // SamplingParameters includes Tags (early attributes) at span creation
        var method = GetTag(parameters.Tags, "http.request.method") as string;
        var route = GetTag(parameters.Tags, "http.route") as string;

        // Only apply to HTTP server spans
        if (method is not null)
        {
            return method.ToUpperInvariant() switch
            {
                "POST" or "PUT" or "PATCH" or "DELETE" => new SamplingResult(SamplingDecision.RecordAndSample),
                _ => _readSampler.ShouldSample(parameters),
            };
        }

        return _defaultSampler.ShouldSample(parameters);
    }

    public override string Description => "RouteAwareSampler (writes=100%, reads=10%)";

    private static object? GetTag(IEnumerable<KeyValuePair<string, object?>>? tags, string name)
        => tags?.FirstOrDefault(t => t.Key == name).Value;
}
```

Register it in `AddMyAppObservability`:

```csharp
otel.WithTracing(t => t
    .AddSource("Wolverine")
    .AddSource("MyApp.Catalog")
    .AddSource("MyApp.Orders")
    .AddSource("MyApp.Search")
    .AddSource("MyApp.Invoices")
    .AddSource("MyApp.Shipping")
    .SetSampler(new RouteAwareSampler())   // ← custom head sampler
    .AddAspNetCoreInstrumentation()
    // ...
);
```

**Caveat:** head sampling decides **before** the span has attributes like `http.response.status_code` or `error`. You can't head-sample "all errors" because you don't know yet if the request will error. That's what tail sampling is for.

### F.3 Custom sampler — when + how

Beyond the route-aware pattern above, custom samplers are useful for:

1. **Always sample specific aggregates** — e.g. every operation on `Order` with `Id = <known important order>`
2. **Always sample specific tenants** — e.g. enterprise customers on SLA contracts
3. **Sample based on baggage** — e.g. `feature.new_pricing=true` traces are always kept

```csharp
// Sample based on baggage — always keep traces for the "new pricing" feature flag
public sealed class FeatureFlagSampler : Sampler
{
    private readonly Sampler _inner = new TraceIdRatioBasedSampler(0.1);

    public override SamplingResult ShouldSample(in SamplingParameters parameters)
    {
        // Baggage is available at span creation (set by middleware before the handler runs)
        var featureFlag = Baggage.Current.GetBaggage("feature.new_pricing");
        if (featureFlag == "true")
            return new SamplingResult(SamplingDecision.RecordAndSample);

        return _inner.ShouldSample(parameters);
    }

    public override string Description => "FeatureFlagSampler (new_pricing=100%, rest=10%)";
}
```

### F.4 Tail sampling — recap

Tail sampling runs in the **collector gateway** (TM §6.2), after the full trace is complete. It can see every span, every attribute, the final status, and the total duration. This lets you write policies like "keep 100% of errors, 100% of slow traces, 10% of everything else".

```yaml
# From TM §6.2 — tail_sampling processor config
tail_sampling:
  decision_wait: 10s          # wait for the trace to complete before deciding
  num_traces: 50000           # in-memory buffer of recent traces
  policies:
    - name: errors
      type: status_code
      status_code: { status_codes: [ERROR] }     # 100% of error traces

    - name: slow
      type: latency
      latency: { threshold_ms: 500 }             # 100% of traces slower than 500ms

    - name: writes
      type: string_attribute
      string_attribute:
        key: http.request.method
        values: [POST, PUT, PATCH, DELETE]       # 100% of write traces

    - name: baseline
      type: probabilistic
      probabilistic: { sampling_percentage: 10 } # 10% of everything else
```

**The order matters:** a trace matching *any* policy is kept. So errors are kept at 100% (first policy), and the 10% baseline catches the rest.

**Architecture rule:** use **head sampling** for volume reduction at the source (ApiHost emits fewer spans); use **tail sampling** at the collector gateway for smart filtering (keep interesting traces, drop boring ones). The kit uses tail sampling only — head sampling is off (default `AlwaysOn`) because dev/staging traffic is low.

---

## Part G — Production concepts the course does not cover

The course is an instrumentation course. It does not teach how to operate a telemetry backend. These are the gaps a production design must close:

| # | Concept | Why it matters |
| --- | --- | --- |
| 1 | Semantic conventions and versioning | Attribute names and meanings change; pin the convention version and review migrations. |
| 2 | Metric temporality, units, buckets | Cumulative vs delta, correct units, SLO-aligned buckets; wrong temporality produces wrong rates. |
| 3 | Exemplars end to end | Requires the SDK, collector, Prometheus, and Grafana datasource to all preserve them. |
| 4 | Collector self-observability | `otelcol_*` metrics for queue depth, refused/dropped data, export failures, and backpressure. |
| 5 | Cardinality as a system | Label budgets, relabeling, series churn, pod/instance labels, per-tenant limits. |
| 6 | Log practice | Severity mapping, structured fields, volume control, log sampling, redaction in the log pipeline. |
| 7 | Sampling architecture | Head vs tail interaction, trace-id-aware tail load balancing, decision-wait trade-offs, clock skew. |
| 8 | Security and privacy | TLS/mTLS, backend auth, PII classification, data minimisation, retention/deletion, access control. |
| 9 | SLO operating model | SLI selection, error-budget policy, burn-rate tiers, escalation, runbooks, postmortems. |
| 10 | Capacity and cost | Ingest estimation, retention math, storage sizing, collector scaling. |
| 11 | HA, DR, and upgrades | Backend replication, queue durability, backup/restore tests, schema migrations, rollback. |
| 12 | Governance | Telemetry as code, naming registry, ownership, deprecation, review rules. |

Until these are designed and tested, the production guide is a target, not a claim.

## Part H — Recommended learning path

If you're working through the course alongside the kit docs:

| Course section | Read in kit docs | Then implement |
|---|---|---|
| 02 Intro to Observability | This doc Part B | Nothing — conceptual |
| 03–04 Auto instrumentation | `TELEMETRY-DEV-QUICKSTART.md` §2–3 | Bring up dev stack, wire ApiHost |
| 05 Manual instrumentation | This doc Part C + TM §8.5 | Add `CatalogMetrics` to Products module |
| 06 Tracing | TM §9 | Verify traces in Tempo |
| 07 Metrics | This doc Part C §5 + TM §8 | Add UpDownCounter to OrdersApi |
| 08 Collector | `TELEMETRY-DEV-QUICKSTART.md` §2.2 + TM §6 | Switch from dev collector to agent+gateway |
| 09 Logging | TM §11 | Verify logs in Loki with trace_id |
| 10 Context Propagation | **This doc Part D** | Add `UserContextBaggageMiddleware` to ApiHost |
| 11 Deep diving into Tracing | **This doc Part E** | Add `ErrorTracingBehavior` to pipeline |
| 12 Sampling | **This doc Part F** + TM §6.2 | Add `RouteAwareSampler` + tail sampling policies |

**Three docs, one stack:**

```
TELEMETRY-DEV-QUICKSTART.md  ← 30 min, see it work
        │
        ▼
TELEMETRY.md                 ← prod-ready, comprehensive
        │
        ▼
TELEMETRY-CONCEPTS.md        ← this doc — the "why" + advanced patterns
   (Part D baggage + Part E error tracing + Part F sampling
    are the highest-leverage additions for the MyApp kit)
```

---

## Part I — Top 5 patterns to actually adopt

If you only adopt five things from this companion, make them these:

### 1. `UserContextBaggageMiddleware` (Part D.2)
Propagate `user.id` + `tenant.id` from JWT through Wolverine to every consumer. Every log line across ApiHost + OrdersApi + InvoicesApi + ShippingApi becomes filterable by tenant without a DB join.

### 2. `ErrorTracingBehavior` (Part E.3)
Map `ErrorOr<T>` results to span status. Every business failure (`PRODUCT.NOT_FOUND`, `AUTH.INVALID_CREDENTIALS`) produces a red span in Tempo with the error code as an attribute. TraceQL `status = error` instantly shows all business failures across all services.

### 3. `PiiRedactionProcessor` (Part E.1)
Redact emails + JWTs from span attributes before export. Keeps PII out of Tempo without changing handler code.

### 4. Domain events as span events (Part E.2)
Add an `ActivityEvent` whenever `AddDomainEvent` is called. The trace timeline in Tempo shows exactly when each domain event fired relative to the handler's work — invaluable for debugging event ordering.

### 5. `RouteAwareSampler` + tail sampling (Part F.2 + TM §6.2)
Always sample writes, 10% of reads at the source; always keep errors + slow traces at the collector. You get full trace coverage for business-critical operations at 1/10th the volume.

---

## Appendix — concept-to-file quick reference

| Concept | Where in the kit |
|---|---|
| Baggage middleware | `src/Shared/MyApp.Observability/UserContextBaggageMiddleware.cs` (new) |
| Baggage read in consumer | Any OrdersApi/InvoicesApi/ShippingApi handler |
| Custom propagator | `src/Shared/MyApp.Observability/LegacyCorrelationPropagator.cs` (new, only if needed) |
| PII redaction processor | `src/Shared/MyApp.Observability/PiiRedactionProcessor.cs` (new) |
| Error tracing behavior | `src/Shared/Application/Behaviors/ErrorTracingBehavior.cs` (new) |
| Domain event span events | `src/Modules/Catalog/Products/Product.cs` (extend `Create`) |
| Manual span links | Only if you bypass Wolverine (not recommended for the kit) |
| Route-aware sampler | `src/Shared/MyApp.Observability/RouteAwareSampler.cs` (new) |
| UpDownCounter for orders | `src/Services/MyApp.OrdersApi/Orders/OrderMetrics.cs` (new) |
| ObservableGauge for search | `src/Modules/Search/SearchMetrics.cs` (new) |

All new files follow the kit's existing layering rules (TM §9): telemetry primitives live in `src/Shared/MyApp.Observability/`, behaviors in `src/Shared/Application/Behaviors/`, module-specific metrics in the module's area.
