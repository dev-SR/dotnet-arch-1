# OpenTelemetry + Grafana Stack — Setup & Usage Guide

> **Scope:** Adding end-to-end observability (metrics, logs, traces) to the `.NET 10` modular monolith kit (`MyApp.ApiHost` + extracted services `OrdersApi` / `InvoicesApi` / `ShippingApi` / `Gateway`), with the **Grafana stack** as the backend (Prometheus, Loki, Tempo, Grafana, Alertmanager) and an **OpenTelemetry Collector** in **agent + gateway** topology.
>
> **Status: production target, not verified.** This document describes the intended production topology and operating model. It is **not** a record of a working deployment. The runnable path is the dev quickstart (validated against package APIs and repo topology, but not yet executed end to end); treat every config and query here as a design that must be validated (`otelcol validate`, `promtool check`, a real cluster) before it is called production-ready.
>
> **Open gaps:** TLS/mTLS, authentication on backends, HA, backup/restore, retention and cost math, multi-tenancy, and the SLO operating model are not yet designed. They are tracked in §28.
>
**How to read this guide:** §5–§6 explain the containers and collector topology; §7 explains what the .NET host emits; §8–§10 explain the signals; §13–§16 cover rules, alerts, SLOs, and dashboards; §18 is deployment. Each configuration section starts with the purpose and a key-setting table before the file.

> **Architecture rules:** telemetry wiring is host-level and belongs in `Infrastructure/` (composition) and `Presentation/` (middleware). `Program.cs` may only add a single `Add*` call to the chain. Shared telemetry code lives in a host-only `Shared/MyApp.Observability` project, never in the shared kernel that modules reference.

---

## 0. TL;DR — what you'll have at the end

| Signal | Source | Transport | Storage | UI |
|---|---|---|---|---|
| **Metrics** | ASP.NET Core, HttpClient, runtime, custom `System.Diagnostics.Metrics` | OTLP → Collector → Prometheus `remote_write` | Prometheus | Grafana dashboards |
| **Logs** | `Microsoft.Extensions.Logging` → OTLP logs SDK | OTLP → Collector → Loki OTLP endpoint | Loki (TSDB) | Grafana → Explore → Logs |
| **Traces** | `System.Diagnostics.Activity` (ASP.NET Core, HttpClient, Wolverine) | OTLP → Collector → Tempo OTLP | Tempo (S3 / local FS) | Grafana → Explore → Traces |

All three signals carry the **same `trace_id`** so you can pivot from a metric → log → trace → span with one click.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                       .NET hosts (ApiHost, OrdersApi, …)                      │
│  ASP.NET Core │ HttpClient │ EF Core │ Wolverine │ custom Activity / Metrics │
│                          │ OTLP/gRPC :4317                                   │
└──────────────────────────┼───────────────────────────────────────────────────┘
                           ▼
            ┌──────────────────────────────────┐
            │  otel-collector-agent (sidecar / │   per-host, low latency,
            │  DaemonSet)                      │   batch+retry, resource enrich
            └──────────────┬───────────────────┘
                           ▼ OTLP/gRPC :4317
            ┌──────────────────────────────────┐
            │  otel-collector-gateway          │   cluster-level, fan-out,
            │  (Deployment)                    │   filter, routing, retries
            └───┬───────────────┬───────────┬──┘
                ▼               ▼           ▼
        Prometheus         Loki          Tempo
        (remote_write)     (OTLP)        (OTLP)
                │               │           │
                └───────┬───────┴───────┬───┘
                        ▼               ▼
                    Grafana ◄──── Alertmanager
```

---

## 1. Goals & non-goals

### Goals

1. **One pipeline, three signals** — metrics, logs, traces all go through OTLP into one Collector.
2. **Trace/log correlation** — every log record carries `trace_id` / `span_id`; every span carries `service.name` + `service.version`; every Prometheus time series carries the same `service` label.
3. **No vendor lock-in** — Collector + OTLP only; backends are swappable.
4. **Same code path in dev and prod** — only config (endpoint URLs, sampling, retention) differs.
5. **SLOs + alerts** — defined in code (recording rules + Prometheus rules), not in someone's head.
6. **Minimal app code churn** — one `AddMyAppObservability(...)` extension per host; no module changes required.

### Non-goals

- Replacing `Microsoft.Extensions.Logging` with Serilog. (You can, but the OTLP logs SDK is enough and matches the kit's "no extra moving parts" philosophy.)
- Custom dashboards per module. (You get one canonical service dashboard per service; module-level detail comes from labels/attributes, not new dashboards.)
- APM-style paid features.

---

## 2. Prerequisites

| Thing | Version | Why |
|---|---|---|
| .NET SDK | `10.0.x` | The kit targets `net10.0`. OTel packages below target `net8.0+`. |
| Docker / Docker Compose | `24+` / `v2+` | Local stack. |
| `otelcol` contrib collector | `0.110.0+` | `contrib` distro is required for `prometheusremotewrite` and `loki` exporters. |
| Prometheus | `2.55+` | Native OTLP receiver is in `2.47+`; `2.55+` adds OTLP-with-exemplars. |
| Loki | `3.2+` | Native OTLP `/otlp/v1/logs` endpoint. |
| Tempo | `2.6+` | OTLP receiver + TraceQL metrics generator. |
| Grafana | `11.3+` | Loki datasource v2 + Tempo datasource with native correlation. |
| Alertmanager | `0.27+` | Routes + silences via UI. |
| OS ports free | `3000` (Grafana), `9090` (Prom), `3100` (Loki), `3200` (Tempo), `4317/4318` (OTLP), `9093` (AM) | Avoid collisions with the kit's existing `5101` (ApiHost) / RabbitMQ `5672/15672` / ES `9200`. |

> Linux/Mac users: nothing special. Windows users: WSL2 + Docker Desktop.

---

## 3. Repository layout additions

This guide adds a top-level `observability/` folder and a few files per host. Nothing in `src/Shared/` changes — observability is host-level, not kit-level (the kit primitives stay transport-agnostic).

```text
backend/
├── observability/                      ← NEW — all infra configs live here
│   ├── docker-compose.observability.yml
│   ├── otel-collector/
│   │   ├── agent.yaml                  ← per-host collector (Batch + Resource + Retry)
│   │   ├── gateway.yaml                ← cluster collector (Filter + Fan-out)
│   │   └── prometheus-relay.yaml        ← dev-only single-collector shortcut
│   ├── prometheus/
│   │   ├── prometheus.yml
│   │   ├── rules/
│   │   │   ├── service-recording.yml    ← recording rules (SLO input)
│   │   │   └── service-alerting.yml     ← alert expressions
│   ├── alertmanager/
│   │   └── alertmanager.yml
│   ├── loki/
│   │   └── loki.yml
│   ├── tempo/
│   │   ├── tempo.yml
│   │   └── overrides.yml
│   └── grafana/
│       ├── grafana.ini
│       ├── provisioning/
│       │   ├── datasources/
│       │   │   └── datasources.yml
│       │   └── dashboards/
│       │       ├── dashboards.yml
│       │       ├── service-overview.json
│       │       ├── http-latency.json
│       │       └── slo.json
└── src/
    ├── Shared/
    │   └── MyApp.Observability/                 ← NEW — host-only shared project
    │       ├── MyApp.Observability.csproj
    │       ├── ObservabilityServiceCollectionExtensions.cs
    │       └── BaggageMiddleware.cs
    └── Services/
        ├── MyApp.ApiHost/                       ← references MyApp.Observability
        ├── MyApp.Gateway/
        ├── MyApp.InvoicesApi/
        ├── MyApp.OrdersApi/
        └── MyApp.ShippingApi/
```

The shared project is referenced by the hosts only. Do **not** add OpenTelemetry to `MyApp.Modular` or `Shared.Application`: every module references those, and telemetry must not become part of the module kernel. **Do not** put telemetry DI or middleware in `Program.cs` — see `ARCHITECTURE.md §12.1`.

---

## 4. Package additions (central package management)

Add to **`Directory.Packages.props`** under the existing `<ItemGroup>`:

```xml
<!-- OpenTelemetry host + SDK (verified on net10.0) -->
<PackageVersion Include="OpenTelemetry" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.17.0" />

<!-- Auto-instrumentation -->
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.17.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" Version="1.13.0-beta.1" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.15.1" />
```

There is **no** `WolverineFx.OpenTelemetry` package. Wolverine emits activities on the `"Wolverine"` source and metrics on `Wolverine:{ApplicationName}`; register them with `AddSource("Wolverine")` and `AddMeter("Wolverine*")`.

Then in **every host csproj** (`MyApp.ApiHost.csproj`, `MyApp.OrdersApi.csproj`, etc.) add the references — keep them at the **host** level only, not in modules:

```xml
<ItemGroup>
  <PackageReference Include="OpenTelemetry.Extensions.Hosting" />
  <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" />
  <PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Http" />
  <PackageReference Include="OpenTelemetry.Instrumentation.EntityFrameworkCore" />
  <PackageReference Include="OpenTelemetry.Instrumentation.Runtime" />
</ItemGroup>
```

`Console` exporter is dev-only — gate it behind `IsDevelopment()`.

---

## 5. Infrastructure — `docker-compose.observability.yml`

Drop this file next to the existing `docker-compose.yml`. Run the kit's existing compose for RabbitMQ + ES, then this one for the observability stack. (You can also merge them; keeping them split lets you start the stack only when you need it.)

**What each container is for:**

| Container | Job | Key port |
| --- | --- | --- |
| `otel-agent` | Receive OTLP from apps, enrich, batch, forward | `4317` / `4318` |
| `otel-gateway` | Tail-sample, filter, fan out to backends | internal `4317`; `8889` self-metrics |
| `prometheus` | Store metrics, evaluate rules | `9090` |
| `loki` | Store logs | `3100` |
| `tempo` | Store traces | `3200` |
| `grafana` | Query and correlate | `3000` |
| `alertmanager` | Route alerts | `9093` |

The `app` network is external because the app containers (or host processes) already exist. The agent joins both networks so it can receive from apps and forward to the observability network.


```yaml
# observability/docker-compose.observability.yml
networks:
  obs:
    name: observability
  app:
    external: true            # the network your existing docker-compose creates
                             # (rename to match yours; default is "backend_default" if you
                             #  used `docker compose up` from /backend)

volumes:
  prometheus-data:
  loki-data:
  tempo-data:
  grafana-data:
  alertmanager-data:

services:

  # ─── 5.1  otel-collector agent (per-host) ───────────────────────────────
  # In K8s this is a DaemonSet; in docker-compose it's a single shared collector
  # (one process is plenty for dev — the agent/gateway split still matters in prod).
  otel-agent:
    image: otel/opentelemetry-collector-contrib:0.110.0
    container_name: otel-agent
    command: ["--config=/etc/otelcol/agent.yaml"]
    volumes:
      - ./otel-collector/agent.yaml:/etc/otelcol/agent.yaml:ro
    ports:
      - "4317:4317"   # OTLP/gRPC  ← apps point here
      - "4318:4318"   # OTLP/HTTP
      - "8888:8888"   # internal metrics
    networks: [obs, app]
    depends_on: [otel-gateway]
    restart: unless-stopped

  # ─── 5.2  otel-collector gateway (cluster-level fan-out) ───────────────
  otel-gateway:
    image: otel/opentelemetry-collector-contrib:0.110.0
    container_name: otel-gateway
    command: ["--config=/etc/otelcol/gateway.yaml"]
    volumes:
      - ./otel-collector/gateway.yaml:/etc/otelcol/gateway.yaml:ro
    ports:
      - "14317:4317"  # expose a different host port so apps don't bypass the agent
      - "8889:8889"   # gateway internal metrics
    networks: [obs]
    depends_on: [prometheus, loki, tempo]
    restart: unless-stopped

  # ─── 5.3  Prometheus (remote-write receiver) ───────────────────────────
  prometheus:
    image: prom/prometheus:v2.55.1
    container_name: prometheus
    command:
      - --config.file=/etc/prometheus/prometheus.yml
      - --storage.tsdb.path=/prometheus
      - --storage.tsdb.retention.time=15d
      - --web.enable-remote-write-receiver
      - --enable-feature=exemplar-storage
    volumes:
      - ./prometheus/prometheus.yml:/etc/prometheus/prometheus.yml:ro
      - ./prometheus/rules:/etc/prometheus/rules:ro
      - prometheus-data:/prometheus
    ports: ["9090:9090"]
    networks: [obs]
    restart: unless-stopped

  # ─── 5.4  Loki (OTLP native logs) ──────────────────────────────────────
  loki:
    image: grafana/loki:3.2.1
    container_name: loki
    command: -config.file=/etc/loki/loki.yml
    volumes:
      - ./loki/loki.yml:/etc/loki/loki.yml:ro
      - loki-data:/loki
    ports: ["3100:3100"]
    networks: [obs]
    restart: unless-stopped

  # ─── 5.5  Tempo (OTLP traces + TraceQL metrics) ────────────────────────
  tempo:
    image: grafana/tempo:2.6.0
    container_name: tempo
    command:
      - -config.file=/etc/tempo/tempo.yml
    volumes:
      - ./tempo/tempo.yml:/etc/tempo/tempo.yml:ro
      - ./tempo/overrides.yml:/etc/tempo/overrides.yml:ro
      - tempo-data:/var/tempo
    ports:
      - "3200:3200"   # Tempo HTTP API / TraceQL (UI is Grafana)
      - "14268:14268" # Jaeger ingest (legacy)
    networks: [obs]
    restart: unless-stopped

  # ─── 5.6  Grafana ──────────────────────────────────────────────────────
  grafana:
    image: grafana/grafana:11.3.0
    container_name: grafana
    environment:
      GF_SECURITY_ADMIN_USER: admin
      GF_SECURITY_ADMIN_PASSWORD: admin
      GF_USERS_ALLOW_SIGN_UP: "false"
      GF_AUTH_ANONYMOUS_ENABLED: "false"
    volumes:
      - ./grafana/grafana.ini:/etc/grafana/grafana.ini:ro
      - ./grafana/provisioning:/etc/grafana/provisioning:ro
      - grafana-data:/var/lib/grafana
    ports: ["3000:3000"]
    networks: [obs]
    depends_on: [prometheus, loki, tempo, alertmanager]
    restart: unless-stopped

  # ─── 5.7  Alertmanager ─────────────────────────────────────────────────
  alertmanager:
    image: prom/alertmanager:v0.27.0
    container_name: alertmanager
    command:
      - --config.file=/etc/alertmanager/alertmanager.yml
      - --storage.path=/alertmanager
    volumes:
      - ./alertmanager/alertmanager.yml:/etc/alertmanager/alertmanager.yml:ro
      - alertmanager-data:/alertmanager
    ports: ["9093:9093"]
    networks: [obs]
    restart: unless-stopped
```

Bring it up:

```bash
cd observability
docker compose -f docker-compose.observability.yml up -d
```

Sanity check:

```bash
# Prometheus should list itself as up
curl -s http://localhost:9090/-/healthy
# Loki alive
curl -s http://localhost:3100/ready
# Tempo ready
curl -s http://localhost:3200/ready
# Grafana → http://localhost:3000 (admin/admin)
```

---

## 6. Collector configurations

### Why two collectors (agent + gateway)?

In the dev quickstart, one collector handles everything. In production, you split into **agent** (per-host) and **gateway** (cluster-level) for four reasons:

1. **Latency** — the agent runs next to your app (same host, same Kubernetes node). It batches + retries locally, so a network blip to the gateway doesn't drop data. The app's OTLP export call returns in <1ms.

2. **Enrichment** — the agent knows things the app shouldn't: which Kubernetes node it's on, which cloud region, which datacenter. It adds these as resource attributes before forwarding. The app code stays clean.

3. **Heavy processing** — tail sampling (keep 100% of errors, 10% of normal traffic) needs to see the complete trace before deciding. That only works at a point where all spans for a trace converge — the gateway. The agent just forwards; the gateway samples.

4. **Fan-out** — the gateway sends traces to Tempo AND Jaeger, metrics to Prometheus AND Datadog. The app only knows about the agent; the agent only knows about the gateway. Adding a new backend is a gateway config change, not a redeploy of every service.

```
App → Agent (per host)     → Gateway (cluster)     → Tempo, Loki, Prometheus
      batch + retry              filter + sample       storage
      enrich with host info      fan-out to N backends
      <1ms export                queue on backend down
```

If you're thinking "this is overkill for my team size" — for a single-node deployment, run one collector with the agent config. It does both jobs. The split only matters when you have multiple hosts or a Kubernetes cluster.

### 6.1 Agent — `otel-collector/agent.yaml`

**What the agent is for:** it runs close to the app (same host or Kubernetes node), receives OTLP, adds host/cluster information the app cannot know, batches, and forwards to the gateway. It is the first line of defence against a backend outage.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `memory_limiter` (processor) | Refuses data before the process runs out of memory | A crashed collector loses everything in flight |
| `resourcedetection` | Adds `os.*`, `host.*`, `process.*` from the environment | The app should not hard-code host metadata |
| `resource/enrich` | Adds environment, host, cloud provider/region | Makes every signal filterable by deployment context |
| `filter/drop_healthchecks` | Drops `/health` and OpenAPI spans | Health checks are high-volume and low-value |
| `batch` | Groups data before sending | Fewer network calls; lower overhead |
| `sending_queue` (on the exporter) | Buffers when the gateway is down | Queueing belongs on the exporter, not as a processor |
| `storage: file_storage` | Makes the queue survive collector restarts | Prevents data loss during a restart |
| `retry_on_failure` | Retries with backoff | Turns a transient outage into delay, not loss |
| `prometheus` exporter on `:8888` | Exposes the agent’s own metrics | Lets you alert on queue depth and dropped data |

The important architectural point: the agent does **not** fan out to Tempo/Loki/Prometheus. It forwards to the gateway. That keeps app configuration simple and lets the gateway change without touching every host.


The **agent** runs next to (or inside the same network as) each app. Its job: receive OTLP, **enrich** with resource attributes the app can't know (datacenter, cloud region, k8s node), **batch**, **retry** on queue full, and **forward** to the gateway over OTLP/gRPC.

```yaml
# observability/otel-collector/agent.yaml
extensions:
  health_check:
    endpoint: 0.0.0.0:13133
  file_storage:
    create_directory: true
    directory: /var/lib/otelcol/storage
    timeout: 1s

receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
        max_recv_msg_size_mib: 4
      http:
        endpoint: 0.0.0.0:4318

  # Host metrics (non-Kubernetes). In Kubernetes, scrape the kubelet on the gateway.
  hostmetrics:
    collection_interval: 30s
    scrapers:
      cpu: {}
      disk: {}
      filesystem: {}
      load: {}
      memory: {}
      network: {}
      processes: {}

processors:
  memory_limiter:
    check_interval: 1s
    limit_percentage: 80
    spike_limit_percentage: 25

  resourcedetection:
    detectors: [env, system, host]
    timeout: 2s
    override: false

  resource/enrich:
    attributes:
      - key: deployment.environment
        value: ${env:DEPLOYMENT_ENV:dev}
        action: upsert
      - key: host.name
        value: ${env:HOSTNAME:unknown}
        action: upsert
      - key: cloud.provider
        value: ${env:CLOUD_PROVIDER:onprem}
        action: upsert
      - key: cloud.region
        value: ${env:CLOUD_REGION:local}
        action: upsert

  filter/drop_healthchecks:
    error_mode: ignore
    traces:
      span:
        - 'attributes["http.route"] == "/health" or attributes["http.route"] == "/alive"'
        - 'attributes["http.target"] == "/health"'
        - 'attributes["http.target"] == "/openapi/v1.json"'

  batch:
    timeout: 5s
    send_batch_size: 8192
    send_batch_max_size: 10000

exporters:
  otlp/gateway:
    endpoint: otel-gateway:4317
    tls:
      insecure: true
    # Queueing belongs on the exporter, not as a processor.
    sending_queue:
      enabled: true
      num_consumers: 8
      queue_size: 5000
      storage: file_storage
    retry_on_failure:
      enabled: true
      initial_interval: 1s
      max_interval: 30s
      max_elapsed_time: 300s

  prometheus:
    endpoint: 0.0.0.0:8888
    namespace: otelcol_agent

service:
  extensions: [health_check, file_storage]
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, resourcedetection, resource/enrich, filter/drop_healthchecks, batch]
      exporters: [otlp/gateway]
    metrics:
      receivers: [otlp, hostmetrics]
      processors: [memory_limiter, resourcedetection, resource/enrich, batch]
      exporters: [otlp/gateway]
    logs:
      receivers: [otlp]
      processors: [memory_limiter, resource/enrich, batch]
      exporters: [otlp/gateway]
```

### 6.2 Gateway — `otel-collector/gateway.yaml`

**What the gateway is for:** it is the cluster chokepoint. It sees all spans for a trace, which is why tail sampling runs here. It also fans out to the three backends and does the heavy filtering.

**Key settings and why they matter:**

| Setting | What it does | Why this value |
| --- | --- | --- |
| `tail_sampling` | Decides after the trace completes | It can see status and duration, so it can keep errors and slow traces |
| `decision_wait: 10s` | Waits for the trace to finish | Too short drops late spans; too long increases memory and latency |
| `num_traces` | In-memory trace buffer | Must cover `expected_new_traces_per_sec × decision_wait` with headroom |
| `policies` | Error, latency, then probabilistic baseline | The first matching policy wins; errors are kept before the 10% baseline |
| `prometheusremotewrite` | Pushes metrics to Prometheus | Prometheus must run with `--web.enable-remote-write-receiver` |
| `resource_to_telemetry_conversion` | Turns `service.name` into a label | Without it, all services collapse into one series |
| `otlphttp/loki` | Sends logs to Loki over OTLP/HTTP | Loki’s OTLP HTTP endpoint is `:3100/otlp` |
| `otlp/tempo` | Sends traces to Tempo | Tempo’s OTLP/gRPC receiver is `:4317` |
| `prometheus` on `:8889` | Exposes gateway self-metrics | Needed to alert on queue depth and dropped data |

**Tail sampling in one sentence:** the gateway holds a trace for `decision_wait`, then asks “is it an error, is it slow, or should I keep a 10% sample?” and keeps it if any policy says yes. That is how you keep 100% of errors without storing 100% of traffic.


The **gateway** is the cluster chokepoint. It does the **heavy filtering**, **routing**, and **fan-out** to the three backends. Apps never talk to it directly in prod — the agent always sits between them.

```yaml
# observability/otel-collector/gateway.yaml
extensions:
  health_check:
    endpoint: 0.0.0.0:13133

receivers:
  otlp:
    protocols:
      grpc:
        endpoint: 0.0.0.0:4317
        max_recv_msg_size_mib: 16
      http:
        endpoint: 0.0.0.0:4318

processors:
  memory_limiter:
    check_interval: 1s
    limit_percentage: 80
    spike_limit_percentage: 25

  resource/cluster:
    attributes:
      - key: k8s.cluster.name
        value: ${env:K8S_CLUSTER:local}
        action: upsert

  filter/noise:
    error_mode: ignore
    traces:
      span:
        - 'attributes["http.route"] == "/health" or attributes["http.route"] == "/alive"'
        - 'attributes["http.target"] == "/health"'

  # Tail sampling runs here because the gateway sees all spans for a trace.
  tail_sampling:
    decision_wait: 10s
    num_traces: 50000
    expected_new_traces_per_sec: 1000
    policies:
      - name: errors
        type: status_code
        status_code:
          status_codes: [ERROR]
      - name: slow
        type: latency
        latency:
          threshold_ms: 500
      - name: baseline
        type: probabilistic
        probabilistic:
          sampling_percentage: 10

  batch:
    timeout: 2s
    send_batch_size: 10000
    send_batch_max_size: 20000

exporters:
  prometheusremotewrite:
    endpoint: http://prometheus:9090/api/v1/write
    resource_to_telemetry_conversion:
      enabled: true

  # Loki OTLP HTTP. Do not use loki:4317; Loki does not listen there by default.
  otlphttp/loki:
    endpoint: http://loki:3100/otlp

  otlp/tempo:
    endpoint: tempo:4317
    tls:
      insecure: true

  prometheus:
    endpoint: 0.0.0.0:8889
    namespace: otelcol_gateway

service:
  extensions: [health_check]
  pipelines:
    traces:
      receivers: [otlp]
      processors: [memory_limiter, resource/cluster, filter/noise, tail_sampling, batch]
      exporters: [otlp/tempo]
    metrics:
      receivers: [otlp]
      processors: [memory_limiter, resource/cluster, batch]
      exporters: [prometheusremotewrite]
    logs:
      receivers: [otlp]
      processors: [memory_limiter, resource/cluster, batch]
      exporters: [otlphttp/loki]
```

Prometheus must be started with `--web.enable-remote-write-receiver` for `/api/v1/write` to accept data. That flag is in the compose file in §5.

### 6.3 Dev-only shortcut — `otel-collector/prometheus-relay.yaml`

If you don't want the agent/gateway split on your laptop, use a single collector that does everything. **Don't use this in prod.**

```yaml
# observability/otel-collector/prometheus-relay.yaml
# One-collector shortcut for local development.
receivers:
  otlp:
    protocols:
      grpc: { endpoint: 0.0.0.0:4317 }
      http: { endpoint: 0.0.0.0:4318 }

processors:
  memory_limiter:
    check_interval: 1s
    limit_mib: 512
  batch:
    timeout: 1s
    send_batch_size: 1024

exporters:
  otlp/tempo:
    endpoint: tempo:4317
    tls: { insecure: true }
  otlphttp/loki:
    endpoint: http://loki:3100/otlp
  prometheus:
    endpoint: 0.0.0.0:8889
    resource_to_telemetry_conversion:
      enabled: true
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

## 7. .NET instrumentation

### 7.1 Host-only shared project — `MyApp.Observability`

**Why this section exists:** the host is the only place that knows the process identity (`service.name`) and the export endpoint. Modules should not know about collectors or exporters. Keeping the wiring in one host-only project makes that boundary explicit and avoids five slightly different copies.


This is the **single** entry point for telemetry in every host. Put it in a host-only `src/Shared/MyApp.Observability` project (not in `MyApp.Modular`, which every module references), and call it once from each `Program.cs` in the `Add*` chain. Middleware belongs in `Presentation/`, not in `Program.cs`.

**What each instrumentation call does:**

| Call | What it produces | Why it is configured this way |
| --- | --- | --- |
| `ConfigureResource` | `service.name`, version, environment, host | Identity for every signal; set once, never change after deploy |
| `AddSource("Wolverine")` | Producer/consumer spans | Wolverine’s instrumentation is built in and opt-in by source |
| `AddSource("MyApp.Catalog")` etc. | Your custom spans | Exact names; a listener that does not match returns `null` activities |
| `AddAspNetCoreInstrumentation` | Server spans + HTTP metrics | Root span for every request |
| `Filter` on health/OpenAPI | Drops noisy requests | Keeps the trace list and metrics focused on real traffic |
| `AddHttpClientInstrumentation` | Outbound client spans | Shows downstream calls inside the request trace |
| `AddEntityFrameworkCoreInstrumentation` | DB spans | Shows query time under the handler |
| `AddOtlpExporter` (traces) | OTLP batch export | Batching avoids one network call per span |
| `AddMeter("Wolverine*")` | Wolverine metrics | The meter name is `Wolverine:{AppName}`, so wildcard is required |
| `AddMeter("MyApp.Catalog")` etc. | Custom metrics | Exact names only; no wildcard dependency |
| `ExportIntervalMilliseconds = 15000` | Metrics export cadence | Matches Prometheus’s scrape interval |
| `AddLogging(...AddOpenTelemetry...)` | OTLP log records | Logs carry `trace_id`/`span_id` for correlation |

**Why no `SetSampler` here:** dev traffic is low, so the default `ParentBased(AlwaysOn)` keeps every trace. Add a sampler only when you have a volume or cost reason, and read §7.5c first.

**Why no manual propagator:** the default is W3C TraceContext + Baggage. In 1.17 the static setter is not available through the DI builder; use `OTEL_PROPAGATORS` only for a non-default propagator.

```csharp
// src/Shared/MyApp.Observability/ObservabilityServiceCollectionExtensions.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    /// One-stop observability wiring. Call once per host, after
    /// <c>AddApplication()</c> and before <c>AddApi(...)</c> so the OTel
    /// logger provider is registered before any logger is created.
    /// </summary>
    public static IServiceCollection AddMyAppObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName,
        string serviceVersion)
    {
        // ── Context propagator — W3C TraceContext + Baggage (see §7.5a)
        // Enables baggage to flow through Wolverine/RabbitMQ and HttpClient so
        // user.id / tenant.id set in the JWT middleware arrive at every consumer.
        // The default propagator is W3C TraceContext + Baggage.
        // Override with OTEL_PROPAGATORS if needed.

        var otel = services.AddOpenTelemetry();

        // ── Resource builder — these attributes flow into every metric label,
        //    every log attribute, and every span attribute.
        otel.ConfigureResource(r => r
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.MachineName)
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] =
                    configuration["OTEL_DEPLOYMENT_ENVIRONMENT"]
                    ?? (configuration["ASPNETCORE_ENVIRONMENT"]?.ToLowerInvariant() ?? "dev"),
                ["host.name"] = Environment.MachineName,
                ["process.runtime.name"] = "dotnet",
            }));

        // ── Traces ────────────────────────────────────────────────────────
        otel.WithTracing(t => t
            .AddSource("Wolverine")                     // built into Wolverine
            .AddSource("MyApp.Catalog")
            .AddSource("MyApp.Orders")
            .AddSource("MyApp.Search")
            .AddSource("MyApp.Invoices")
            .AddSource("MyApp.Shipping")
            .SetSampler(new RouteAwareSampler())        // §7.5c — 100% writes, 10% reads
            .AddAspNetCoreInstrumentation(o =>
            {
                o.EnrichWithHttpRequest = (activity, request) =>
                {
                    // http.route is added by default; add the endpoint name so
                    // Carter modules show up nicely in Tempo.
                    var endpoint = request.HttpContext.GetEndpoint();
                    activity?.SetTag("myapp.endpoint_name", endpoint?.DisplayName);
                };
                o.Filter = httpContext =>
                {
                    // Drop health/openapi from traces (they're filtered on the
                    // collector too — belt + suspenders).
                    var path = httpContext.Request.Path.Value ?? "";
                    return !path.StartsWith("/health")
                        && !path.StartsWith("/openapi");
                };
            })
            .AddHttpClientInstrumentation(o =>
            {
                // Don't trace Scalar/health pings to the gateway
                o.FilterHttpRequestMessage = msg =>
                    msg.RequestUri is null
                    || !msg.RequestUri.AbsolutePath.StartsWith("/health", StringComparison.OrdinalIgnoreCase);
            })
            .AddEntityFrameworkCoreInstrumentation()
            // The 1.13-beta package does not expose SetDbStatementForText or the
            // stored-procedure switch. Verify the emitted db.* attributes before
            // treating this as PII-safe in production.
            .AddProcessor(new PiiRedactionProcessor())  // §7.5b — redact emails + JWTs from spans
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                    ?? "http://localhost:4317");
                o.Protocol = OtlpExportProtocol.Grpc;
                o.ExportProcessorType = ExportProcessorType.Batch;
                o.BatchExportScheduledDelayMilliseconds = 1000;
            }));

        // ── Metrics ───────────────────────────────────────────────────────
        otel.WithMetrics(m => m
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation()
            .AddMeter("Wolverine*")                     // Wolverine meter is Wolverine:{AppName}
            .AddMeter("MyApp.Catalog")
            .AddMeter("MyApp.Orders")
            .AddMeter("MyApp.Search")
            .AddMeter("MyApp.Invoices")
            .AddMeter("MyApp.Shipping")
            .AddMeter("MyApp.Synthetic")                // synthetic probe (see §21.4)
            .AddView("http.server.request.duration", new ExplicitBucketHistogramConfiguration
            {
                // SLO-aligned buckets: p50, p95, p99 over a 1s budget.
                Boundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10],
            })
            .AddOtlpExporter((exporter, reader) =>
            {
                exporter.Endpoint = new Uri(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                    ?? "http://localhost:4317");
                exporter.Protocol = OtlpExportProtocol.Grpc;
                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = 15000;
            }));

        // ── Logs ─────────────────────────────────────────────────────────
        // OTel logs SDK replaces the default Microsoft.Extensions.Logging
        // formatter; nothing else in your code changes.
        services.AddLogging(logging => logging
            .AddOpenTelemetry(o =>
            {
                o.IncludeFormattedMessage = true;
                o.IncludeScopes = true;
                o.ParseStateValues = true;
                // Add the request-id scope so logs can be grouped by request
                // even when there's no activity (e.g. Wolverine consumers).
                o.AddProcessor(new MyEnvironmentLogEnricher());
                o.AddOtlpExporter(cfg =>
                {
                    cfg.Endpoint = new Uri(configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]
                        ?? "http://localhost:4317");
                    cfg.Protocol = OtlpExportProtocol.Grpc;
                });
            }));

        // ── Wolverine OTel ────────────────────────────────────────────────
        // Wolverine emits activities on the "Wolverine" source and metrics on
        // Wolverine:{ApplicationName}. No package or options are required.
        return services;
    }
}

/// <summary>
/// Adds process-level attributes to every log record so logs that originate
/// outside a request (Wolverine consumers, hosted services) still carry the
/// service identity for filtering.
/// </summary>
internal sealed class MyEnvironmentLogEnricher : BaseProcessor<LogRecord>
{
    public override void OnStart(LogRecord data)
    {
        data.Attributes ??= new Dictionary<string, object?>();
        var attrs = data.Attributes as IDictionary<string, object?>;
        attrs!["process.pid"] = Environment.ProcessId;
        attrs!["deployment.environment"] =
            Environment.GetEnvironmentVariable("OTEL_DEPLOYMENT_ENVIRONMENT") ?? "dev";
    }
}
```

### 7.2 `Program.cs` — one-line composition

In every host's `Program.cs` (ApiHost, OrdersApi, InvoicesApi, ShippingApi, Gateway), add the call **right after `AddInfrastructure`** so logging is configured before any handler or module is wired:

```csharp
// src/Services/MyApp.ApiHost/Program.cs
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMyAppObservability(                              // ← NEW
        builder.Configuration,
        serviceName: "myapp.apihost",
        serviceVersion: typeof(Program).Assembly
            .GetName().Version?.ToString() ?? "0.0.0")
    .AddModularEfCore()
    .AddApi(builder.Configuration)
    .AddUsersModule(builder.Configuration)
    .AddCatalogModule(builder.Configuration)
    .AddSearchModule(builder.Configuration);

builder.UseApiHostMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
// Baggage middleware — after UseSharedPipeline (which calls UseAuthorization)
// so context.User is populated. Sets user.id + tenant.id into Baggage.Current
// for every downstream HTTP call, Wolverine message, and log line. See §7.5a.
app.UseMiddleware<UserContextBaggageMiddleware>();
app.UseUsersModule();
app.UseCatalogModule();
app.UseSearchModule();
app.UseDevelopmentSeeders();
app.MapCarter();

app.Run();

public partial class Program;
```

Repeat the **same pattern** for the extracted services, varying only `serviceName`:

| Host | `serviceName` |
|---|---|
| `MyApp.ApiHost` | `myapp.apihost` |
| `MyApp.Gateway` | `myapp.gateway` |
| `MyApp.OrdersApi` | `myapp.ordersapi` |
| `MyApp.InvoicesApi` | `myapp.invoicesapi` |
| `MyApp.ShippingApi` | `myapp.shippingapi` |

### 7.3 `appsettings.json` additions

```json
{
  "OTEL_EXPORTER_OTLP_ENDPOINT": "http://localhost:4317",
  "OTEL_DEPLOYMENT_ENVIRONMENT": "dev"
}
```

For prod, override via env vars (K8s `deployment.yaml` or `docker run -e`).

### 7.4 `.env` additions (ApiHost already uses DotNetEnv)

Append to `src/Services/MyApp.ApiHost/.env` (and copy to `.env.example`):

```env
# --- OpenTelemetry ---
OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
OTEL_DEPLOYMENT_ENVIRONMENT=dev
OTEL_DOTNET_AUTO_LOGS_FORMAT=structured
OTEL_BLR_DEBUG=false
```

### 7.5 Supporting telemetry files

The `AddMyAppObservability` extension in §7.1 references four files that live in `src/Shared/` so every host picks them up. Create these once; they apply to ApiHost + all extracted services + the Gateway.

#### 7.5a `UserContextBaggageMiddleware` — propagate user/tenant through Wolverine

```csharp
// src/Shared/MyApp.Observability/UserContextBaggageMiddleware.cs
using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace MyApp.Observability;

/// <summary>
/// Copies user.id + tenant.id from JWT claims into Baggage.Current so they
/// propagate to every downstream HTTP call, every Wolverine message, and
/// every log line in this request. Wolverine's OTel integration serializes
/// baggage into RabbitMQ headers automatically (see §10).
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
            var items = new List<KeyValuePair<string, string>>
            {
                new("user.id", userId),
            };

            var tenantId = context.User.FindFirst("tenant_id")?.Value;
            if (!string.IsNullOrEmpty(tenantId))
                items.Add(new("tenant.id", tenantId));

            Baggage.Current = Baggage.Current.SetBaggage(items);
        }

        await _next(context);
    }
}
```

**Why baggage, not span attributes?** Span attributes are read-only after the span ends and only exist on traces (which are sampled). Baggage propagates to **every** signal — logs, metrics attributes, downstream HTTP calls, Wolverine consumers — and you can read it synchronously in any handler without an `Activity` reference.

**Read it in a consumer:**

```csharp
// Any OrdersApi/InvoicesApi/ShippingApi Wolverine handler
var userId = Baggage.Current.GetBaggage("user.id") ?? "unknown";
var tenantId = Baggage.Current.GetBaggage("tenant.id") ?? "default";

logger.LogInformation(
    "Processing order for tenant {TenantId} (triggered by user {UserId})",
    tenantId, userId);
```

The log line in Loki now carries `tenant.id` and `user.id` as structured fields — filterable without joining to the Users module's DB.

**Baggage safety rules:**

| ✓ Put in baggage | ✗ Never put in baggage |
|---|---|
| `user.id` (GUID) | `user.email` (PII) |
| `tenant.id` | JWT tokens |
| `request.id` | Passwords |
| `feature.flag.<name>` | Large JSON blobs |
| | Anything that could explode metric cardinality |

#### 7.5b `PiiRedactionProcessor` — keep PII out of Tempo

A span processor that runs before export and redacts email addresses and JWT-like strings from span attributes. It does **not** redact log bodies or metric labels; those need their own policy (§28).

```csharp
// src/Shared/MyApp.Observability/PiiRedactionProcessor.cs
using OpenTelemetry;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MyApp.Observability;

/// <summary>
/// Span processor that redacts email addresses and JWT tokens from span
/// attributes before export. Registered in AddMyAppObservability (§7.1) via
/// .AddProcessor(new PiiRedactionProcessor()).
/// </summary>
public sealed class PiiRedactionProcessor : BaseProcessor<Activity>
{
    private static readonly Regex JwtPattern =
        new(@"^[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+$",
            RegexOptions.Compiled);

    private static readonly Regex EmailPattern =
        new(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.Compiled);

    public override void OnEnd(Activity activity)
    {
        // Snapshot first: SetTag mutates the collection, so do not enumerate it live.
        foreach (var (key, value) in activity.TagObjects.ToList())
        {
            if (value is string s)
            {
                var redacted = Redact(s);
                if (redacted != s)
                    activity.SetTag(key, redacted);
            }
        }
    }

    private static string Redact(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (JwtPattern.IsMatch(value)) return "<redacted:jwt>";
        return EmailPattern.Replace(value, m =>
            $"{m.Value[..Math.Min(2, m.Value.IndexOf('@'))]}***@{m.Value[(m.Value.IndexOf('@') + 1)..]}");
    }
}
```

**How processors differ from exporters:** a processor runs **per-span** in the SDK (redaction, enrichment, filtering); an exporter runs **per-batch** (transport). Use processors for per-span mutation.

#### 7.5c `RouteAwareSampler` — head sampling by HTTP method

A head sampler that always samples writes (POST/PUT/PATCH/DELETE) and samples reads (GET) at 10%. It is wrapped in `ParentBasedSampler` so a service never keeps a child span whose parent was dropped.

```csharp
// src/Shared/MyApp.Observability/RouteAwareSampler.cs
using OpenTelemetry.Trace;

namespace MyApp.Observability;

public sealed class RouteAwareSampler : Sampler
{
    // ParentBasedSampler: if a remote parent exists, honour its decision.
    // The root sampler is only consulted for new traces.
    private readonly Sampler _sampler = new ParentBasedSampler(new RouteAwareRootSampler());

    public override SamplingResult ShouldSample(in SamplingParameters parameters)
        => _sampler.ShouldSample(parameters);

    public override string Description => "ParentBased(RouteAwareRootSampler)";
}

internal sealed class RouteAwareRootSampler : Sampler
{
    private readonly Sampler _readSampler = new TraceIdRatioBasedSampler(0.1);

    public override SamplingResult ShouldSample(in SamplingParameters parameters)
    {
        var method = parameters.Tags?
            .FirstOrDefault(t => t.Key == "http.method")
            .Value as string;

        if (method is null)
            return new SamplingResult(SamplingDecision.RecordAndSample);

        return method.ToUpperInvariant() switch
        {
            "POST" or "PUT" or "PATCH" or "DELETE"
                => new SamplingResult(SamplingDecision.RecordAndSample),
            _ => _readSampler.ShouldSample(parameters),
        };
    }

    public override string Description => "RouteAwareRootSampler (writes=100%, reads=10%)";
}
```

**Head sampling and tail sampling interact.** Head sampling decides before the request completes, so it cannot know whether the request will fail. If the head sampler drops a trace, the tail sampler in the collector never sees it. Therefore:

- If you need **100% of errors**, do not head-sample traces. Keep the SDK sampler at `AlwaysOn` and let tail sampling decide.
- If you accept sampled errors, head sampling can reduce volume, but the tail policy keeps only the errors that survived.
- Head sampling writes at 100% is fine for volume control, but it does not guarantee error coverage.

The “100% of errors + 10% baseline” policy in §6.2 assumes the SDK is not dropping traces before they reach the gateway.

#### 7.5d `ErrorTracingBehavior` — map `ErrorOr<T>` to span status

A pipeline behavior that runs after every command handler. If the handler returned `ErrorOr<T>` with errors, it sets the active span's status to `Error` and records each error code as a span event. This makes business failures show up as red spans in Tempo, filterable by error code.

```csharp
// src/Shared/Application/Behaviors/ErrorTracingBehavior.cs
using System.Diagnostics;
using Mediator;

namespace Shared.Application.Behaviors;

/// <summary>
/// Pipeline behavior that records ErrorOr results on the active span.
/// Register in Application/DependencyInjection.cs AFTER TransactionBehavior
/// so it runs after the handler returns. Uses reflection to duck-type
/// ErrorOr<T> without modifying Shared.Common.
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

        var activity = Activity.Current;
        if (activity is null) return response;

        // Duck-type ErrorOr<T> via reflection — avoids modifying Shared.Common
        var isErrorProp = response?.GetType().GetProperty("IsError");
        if (isErrorProp?.GetValue(response) is bool isError && isError)
        {
            activity.SetStatus(ActivityStatusCode.Error, "ErrorOr returned errors");

            var errorsProp = response?.GetType().GetProperty("Errors");
            if (errorsProp?.GetValue(response) is System.Collections.IEnumerable errors)
            {
                foreach (var err in errors)
                {
                    var code = err?.GetType().GetProperty("Code")?.GetValue(err)?.ToString() ?? "unknown";
                    var desc = err?.GetType().GetProperty("Description")?.GetValue(err)?.ToString() ?? "";
                    activity.AddEvent(new ActivityEvent("error", new ActivityTagsCollection
                    {
                        new("error.code", code),
                        new("error.description", desc),
                    }));
                    activity.SetTag($"error.{code}", desc);
                }
            }
        }
        else
        {
            // Mark successful spans explicitly — helps Tempo filter "ok" vs "unset"
            activity.SetStatus(ActivityStatusCode.Ok);
        }

        return response;
    }
}
```

Register it in `Application/DependencyInjection.cs`:

```csharp
// src/Services/MyApp.ApiHost/Application/DependencyInjection.cs
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
services.AddPipelineBehavior(typeof(CachingBehavior<,>));
services.AddPipelineBehavior(typeof(TransactionBehavior<,>));
services.AddPipelineBehavior(typeof(ErrorTracingBehavior<,>));   // ← NEW — after Transaction
```

**Result in Tempo:** every `Error.NotFound("PRODUCT.NOT_FOUND", ...)` produces a red span with `error.code = PRODUCT.NOT_FOUND` as an attribute. Filter:

```traceql
{ status = error && span."error.code" = "PRODUCT.NOT_FOUND" }
```

---

## 8. Metrics — what gets exported

### 8.1 Default ASP.NET Core metrics (free)

| Metric | Type | Source |
|---|---|---|
| `http.server.request.duration` | histogram | `OpenTelemetry.Instrumentation.AspNetCore` |
| `http.server.active_requests` | up-down counter | same |
| `http.server.request.body.size` | histogram | same |
| `http.server.response.body.size` | histogram | same |
| `aspnetcore.routing.matches` | counter | same |

Each carries: `http.route`, `http.method`, `http.status_code`, `url.scheme`, `network.protocol.version`.

### 8.2 Runtime + process metrics (free)

GC, ThreadPool, memory, allocations, Kestrel connections — `OpenTelemetry.Instrumentation.Runtime` emits all of them as `process.runtime.dotnet.*`.

The ones worth dashboarding:

- `process.runtime.dotnet.gc.heap.size` (per generation)
- `process.runtime.dotnet.gc.collections.count` (per generation)
- `process.runtime.dotnet.thread_pool.thread.count`
- `process.runtime.dotnet.thread_pool.queue.length` ← headroom indicator
- `process.runtime.dotnet.gc.pause.time` ← STW pressure

### 8.3 EF Core / SQLite metrics

`AddEntityFrameworkCoreInstrumentation` adds **traces**, not metrics. SQLite has no meter, so do not build EF Core dashboards from metrics. Use the EF Core span duration and the `db.*` tags on traces instead.

If you move to PostgreSQL, add the Npgsql meter deliberately and document the provider-specific instrument names; do not assume it matches SQLite.

### 8.4 Wolverine metrics (free)

Wolverine emits metrics on the meter `Wolverine:{ApplicationName}`. Register it with `AddMeter("Wolverine*")` — the wildcard is required because the name includes the application.

Instrument names and tags are Wolverine-version-specific. Inspect the exposed series after sending traffic and build dashboards from the actual names; do not assume names from another version.

Wolverine traces carry messaging tags such as `messaging.message_type`, `messaging.destination`, and `messaging.message_id` on its activities.

### 8.5 Custom metrics — a `MyApp` meter

Add a single meter per module you want to instrument, and register its **exact** name in the host (`AddMeter("MyApp.Catalog")`). Do not rely on wildcards for `AddMeter`/`AddSource`; behaviour differs between OpenTelemetry .NET versions. The exception is Wolverine, whose meter name includes the application name (`AddMeter("Wolverine*")`).

```csharp
// src/Modules/Catalog/Products/CatalogMetrics.cs
using System.Diagnostics.Metrics;

namespace MyApp.Modules.Catalog.Products;

/// <summary>
/// One meter per module. Bound to the module's namespace so consumers stay
/// next to the slice that produces the data — same rule as domain events.
/// </summary>
public static class CatalogMetrics
{
    public static readonly Meter Meter = new("MyApp.Catalog", "1.0.0");

    public static readonly Counter<long> ProductsCreated =
        Meter.CreateCounter<long>("myapp.catalog.products_created");

    public static readonly Counter<long> ProductsDeleted =
        Meter.CreateCounter<long>("myapp.catalog.products_deleted");

    public static readonly Histogram<double> CreateDuration =
        Meter.CreateHistogram<double>("myapp.catalog.create_duration", unit: "ms");
}
```

Use it from a handler (no DI needed — static meter):

```csharp
// src/Modules/Catalog/Products/CreateProduct/CreateProductCommandHandler.cs
internal sealed class CreateProductCommandHandler(CatalogDbContext dbContext)
    : IRequestHandler<CreateProductCommand, ErrorOr<Guid>>
{
    public async ValueTask<ErrorOr<Guid>> Handle(
        CreateProductCommand command, CancellationToken ct)
    {
        using var activity = CatalogActivitySource.Instance.StartActivity("CreateProduct");
        activity?.SetTag("myapp.module", "catalog");
        activity?.SetTag("myapp.slice", "create_product");
        var sw = Stopwatch.StartNew();

        var created = Product.Create(/* ... */);
        if (created.IsError) return ErrorOr<Guid>.From(created.Errors.ToList());

        // Span EVENT — a point-in-time marker on the trace timeline.
        // Shows up in Tempo as a marker labeled "domain.event.raised" at
        // the moment Product.Create added the ProductCreatedDomainEvent.
        activity?.AddEvent(new ActivityEvent("domain.event.raised", DateTimeOffset.UtcNow,
            new ActivityTagsCollection
            {
                new("event.type", "ProductCreated"),
                new("event.product_id", created.Value.Id),
                new("event.product_name", created.Value.Name),
            }));

        dbContext.Products.Add(created.Value);

        CatalogMetrics.ProductsCreated.Add(1,
            new("category", command.Category));
        CatalogMetrics.CreateDuration.Record(sw.Elapsed.TotalMilliseconds);

        // Span status set to Ok — ErrorTracingBehavior (§7.5d) handles the
        // error case if the handler returns ErrorOr with errors.
        activity?.SetStatus(ActivityStatusCode.Ok);
        return created.Value.Id;
    }
}

internal static class CatalogActivitySource
{
    public static readonly ActivitySource Instance = new("MyApp.Catalog", "1.0.0");
}
```

**Span events vs span attributes vs logs:**

| | Span attribute | Span event | Log |
|---|---|---|---|
| When set | During span lifetime | At a specific moment | Anytime |
| What it represents | A property ("http.route=POST /products") | A point-in-time occurrence ("domain event raised at 14:32:01") | A message from the code |
| Where it shows in Tempo | Span detail panel | Span timeline (marker) | Not in Tempo — in Loki |
| Indexed? | Yes (TraceQL tag) | No (visible in span view only) | Yes (Loki label/field) |

Use span events when you want to see **when** something happened within a span's lifetime — domain events raised, validation failed, cache hit/miss — without creating a child span for each.

### 8.6 Custom histogram buckets

In §7.1 we set SLO-aligned buckets for `http.server.request.duration`:

```
5ms · 10ms · 25ms · 50ms · 100ms · 250ms · 500ms · 1s · 2.5s · 5s · 10s
```

This matters: Prometheus' default ASP.NET Core buckets are `[0,5,10,25,...]` and waste buckets on the slow end. Use these to compute **histogram_quantile over p95 = 250ms** cleanly.

### 8.7 UpDownCounter — for stateful counts (OrdersApi)

`Counter<T>` only goes up. For "how many orders are *currently* in `Pending` status?" you'd compute `created - shipped` — fragile and wrong after a restart. An **UpDownCounter** tracks it directly: increments on create, decrements on ship.

```csharp
// src/Services/MyApp.OrdersApi/Orders/OrderMetrics.cs
using System.Diagnostics.Metrics;

namespace MyApp.OrdersApi.Orders;

public static class OrderMetrics
{
    public static readonly Meter Meter = new("MyApp.Orders", "1.0.0");

    // Total orders ever created — only goes up
    public static readonly Counter<long> OrdersCreated =
        Meter.CreateCounter<long>("myapp.orders.created");

    // Current orders in each status — goes up AND down.
    // Increments on PlaceOrder, decrements when order leaves that status.
    public static readonly UpDownCounter<int> ActiveOrders =
        Meter.CreateUpDownCounter<int>("myapp.orders.active");
}
```

Use from the handlers:

```csharp
// PlaceOrderHandler — order created
OrderMetrics.OrdersCreated.Add(1, new("status", "pending"));
OrderMetrics.ActiveOrders.Add(1, new("status", "pending"));

// ShipOrderHandler — order moved from pending → shipped
OrderMetrics.ActiveOrders.Add(-1, new("status", "pending"));
OrderMetrics.ActiveOrders.Add(1, new("status", "shipped"));
```

Prometheus sees `myapp_orders_active{status="pending"}` as a gauge — the current count at scrape time. No subtraction needed, no restart drift.

**The six OTel instrument types at a glance:**

| Instrument | Goes up | Goes down | When to use |
|---|---|---|---|
| `Counter<T>` | ✓ | ✗ | "How many X happened?" (total products created) |
| `UpDownCounter<T>` | ✓ | ✓ | "How many X are currently in state Y?" (active orders) |
| `Histogram<T>` | n/a | n/a | "What's the distribution of X?" (request latency) |
| `ObservableCounter<T>` | ✓ (callback) | ✗ | Counter read from an external source (GC collections) |
| `ObservableGauge<T>` | ✓ (callback) | ✓ (callback) | Gauge read from external source (ThreadPool queue) |

The kit uses `Counter` (§8.5) and `Histogram` (§8.6) in Catalog, `UpDownCounter` (this section) in OrdersApi. Runtime instrumentation provides the `Observable*` variants automatically.

---

## 9. Traces — what gets exported

### 9.1 Auto spans

| Span | Source | Tags you'll see in Tempo |
|---|---|---|
| `POST /products` | ASP.NET Core | `http.route`, `http.method`, `http.status_code`, `myapp.endpoint_name`, `url.scheme` |
| `Products.CreateProduct` | Custom (`CatalogActivitySource`) | `category` |
| `Product.Create` | Custom | depends on tags you add |
| `DbContext.SaveChangesAsync` | EF Core | `db.system=sqlite`, `db.statement=redacted`, `myapp.db.command_type` |
| `Wolverine.Receive ProductCreatedIntegrationEvent` | Wolverine | `wolverine.message_type`, `messaging.destination` |
| `HttpClient POST` (to ES) | HttpClient | `http.request.method`, `server.address`, `url.full` |

### 9.2 Span links vs parent

Wolverine's RabbitMQ integration sets **`LINK`** on the consumer span back to the producer's `trace_id` (W3C `traceparent` propagates through RabbitMQ headers automatically). In Tempo you'll see this as a linked trace, not a child. This is the correct behaviour for async fan-out — read §10 for why.

### 9.3 Custom span attributes — a vocabulary

To keep dashboards greppable across modules, use this convention:

| Attribute | Type | Example |
|---|---|---|
| `myapp.module` | string | `catalog` |
| `myapp.slice` | string | `create_product` |
| `myapp.use_case` | string | `CreateProduct` |
| `myapp.aggregate.id` | string (guid) | `0a65cfdb-...` |
| `myapp.event.fact` | string | `product_created` |
| `myapp.error.code` | string | `PRODUCT.NOT_FOUND` |
| `myapp.tenant.id` | string | (only when you add multi-tenancy) |

Add them with `activity?.SetTag(...)` in handlers and domain-event publishers.

### 9.4 Span events — domain events as timeline markers

A **span event** is a timestamped annotation on a span — like a mini-log that lives *inside* the trace, not in Loki. Events show up in Tempo as markers on the span's timeline. Use them when you want to see **when** something happened within a span's lifetime without creating a child span.

The kit's `Product.Create` raises `ProductCreatedDomainEvent` via `AddDomainEvent`. Add a span event at that moment so the trace timeline shows exactly when the domain event fired:

```csharp
// src/Modules/Catalog/Products/Product.cs (in the Create factory)
public static ErrorOr<Product> Create(string name, decimal price, int stock, Category category, Brand brand)
{
    // ... validation ...

    var product = new Product { /* ... */ };
    product.AddDomainEvent(new ProductCreatedDomainEvent(product.Id, product.Name, product.Price));

    // Span event — visible in Tempo as a marker on the trace timeline
    Activity.Current?.AddEvent(new ActivityEvent("domain.event.raised", DateTimeOffset.UtcNow,
        new ActivityTagsCollection
        {
            new("event.type", "ProductCreated"),
            new("event.product_id", product.Id),
            new("event.product_name", product.Name),
        }));

    return product;
}
```

**Recording exceptions as events:**

```csharp
try
{
    await dbContext.SaveChangesAsync(ct);
}
catch (DbUpdateException ex)
{
    // RecordException adds an event named "exception" with standard
    // OTel attributes: exception.type, exception.message, exception.stacktrace
    Activity.Current?.RecordException(ex);
    throw;
}
```

`RecordException` is a built-in helper that follows the OTel semantic convention for exceptions. Tempo renders it as an error marker on the span.

### 9.5 Error recording — `ErrorOr<T>` → span status

A span has a **status**: `Unset`, `Ok`, or `Error`. Auto-instrumentation sets this for HTTP 5xx and DB failures, but for **business errors** (the kit's `ErrorOr<T>` returning `Error.NotFound(...)`), the `ErrorTracingBehavior` pipeline behavior (§7.5d) sets it automatically.

How it works end-to-end:

```
1. POST /products → ASP.NET Core creates a span (status = Unset)
2. Handler runs Product.Create → returns ErrorOr<Product> with errors
3. ErrorTracingBehavior (§7.5d) inspects the response AFTER the handler
   → if IsError: sets ActivityStatusCode.Error + adds error.code event
   → if success: sets ActivityStatusCode.Ok
4. Span exported to Tempo with status = Error (red span)
```

In Tempo, filter all business errors across all services:

```traceql
{ status = error && span."error.code" =~ "PRODUCT.*" }
```

Or find every auth failure:

```traceql
{ status = error && span."error.code" =~ "AUTH.*" }
```

**Why this matters:** without `ErrorTracingBehavior`, a `POST /products` that returns 400 (validation error) shows up as a green span (HTTP 4xx is not auto-set to Error). With it, every business failure is red and filterable by error code — you can answer "show me every `PRODUCT.NOT_FOUND` in the last hour across all services" with one TraceQL query.

### 9.6 Span links — manual creation

Wolverine creates a consumer activity with the producer's context as its parent (§10). That is a parent/child relationship in the same trace, not a span link. Create links manually only when you integrate a transport or worker that cannot participate in the parent chain.

If you ever need to create a span link manually (e.g. a raw RabbitMQ consumer that bypasses Wolverine):

```csharp
// Extract the producer's context from RabbitMQ headers
var propagator = Propagators.DefaultTextMapPropagator;
var parentContext = propagator.Extract(default, ea.BasicProperties.Headers,
    (headers, name) => headers.TryGetValue(name, out var bytes)
        ? new[] { Encoding.UTF8.GetString((byte[])bytes) }
        : Array.Empty<string>());

// Create a NEW root span LINKED to the producer (not a child)
var link = new ActivityLink(parentContext.ActivityContext);
using var activity = _activitySource.StartActivity(
    "consume.ProductCreated",
    ActivityKind.Consumer,
    new ActivityContext(default, default, default),  // new root, not parentContext
    links: new[] { link });
```

The key is `links: new[] { link }` — that creates a span link instead of a parent/child relationship.

**When to use links vs parent/child:**

| Relationship | Use | Why |
|---|---|---|
| Synchronous call (HTTP → handler → DB) | Parent/child | The child happened *because of* the parent, synchronously |
| Async fan-out (publish → queue → consume later) | Span link | Different time, possibly different host. Parent/child would inflate the producer's duration with the consumer's latency. |
| Batch processing (one trigger → N parallel workers) | Span links | Siblings, not children of each other |

---

## 10. Wolverine + RabbitMQ trace propagation

Wolverine has OpenTelemetry support built in. There is no package to add and no options object to set.

What you must register:

```csharp
otel.WithTracing(t => t
    .AddSource("Wolverine")
    // ... other sources
    );

otel.WithMetrics(m => m
    .AddMeter("Wolverine*")   // meter is Wolverine:{ApplicationName}
    // ... other meters
    );
```

What Wolverine emits:

- a **producer** activity when a message is sent or published (`ActivityKind.Producer`)
- a **consumer** activity when a message is received (`ActivityKind.Consumer`)
- an **execution** activity around the handler
- `traceparent` and `tracestate` in RabbitMQ message headers

### 10.1 Relationship across RabbitMQ

Wolverine starts the consumer activity with the envelope's parent id. That is a **parent/child relationship in the same trace**, not a span link. In Tempo you see one trace with several services:

```
POST /products                 myapp.apihost
├── CreateProduct              myapp.apihost  (myapp.module=catalog)
└── send ProductCreated        myapp.apihost
    ├── receive ProductCreated myapp.apihost  (Search module, in-process)
    └── receive ProductCreated myapp.ordersapi
```

A span's `service.name` is the host resource, not the module name. Use span attributes such as `myapp.module` to distinguish modules inside the monolith.

Do not expect a "Linked traces" panel for this flow. Span links are for sibling relationships (for example a batch job triggered by one event), and Wolverine does not use them for normal pub/sub.

Baggage propagation through Wolverine depends on the version and transport. Do not rely on `user.id` appearing in a consumer unless you have verified it for your Wolverine version; carry business identifiers in the message contract when the consumer needs them.

## 11. Loki configuration — `loki/loki.yml`

```yaml
# observability/loki/loki.yml
auth_enabled: false

server:
  http_listen_port: 3100
  grpc_listen_port: 9096

common:
  instance_addr: 127.0.0.1
  path_prefix: /loki
  storage:
    filesystem:
      chunks_directory: /loki/chunks
      rules_directory: /loki/rules
  replication_factor: 1
  ring:
    kvstore:
      store: inmemory

# OTLP-native ingestion (no LogQL pipeline stages required — the OTel
# resource attributes become stream labels automatically through
# `distributor.otlp_config.default_resource_attributes_as_index_labels` below).
limits_config:
  retention_period: 720h          # 30 days
  max_global_streams_per_user: 5000
  max_line_size: 256000           # 256KiB — fits structured logs
  reject_old_samples: true
  reject_old_samples_max_age: 168h
  allow_structured_metadata: true

# Map OTel resource attrs → Loki stream labels.
# Keep this list small — high-cardinality labels = bad Loki perf.
loki:
  resource_attributes:
    attributes_config:
      - action: index_label
        attributes:
          - service.name
          - deployment.environment
          - service.version
          - host.name
          - k8s.namespace.name
          - k8s.pod.name
      - action: structured_metadata
        attributes:
          - trace_id           # NOT a label — but kept as structured metadata
          - span_id            # for the Grafana log→trace link
          - http.route
          - http.status_code
          - wolverine.message_type

schema_config:
  configs:
    - from: 2024-01-01
      store: tsdb
      object_store: filesystem
      schema: v13
      index:
        prefix: index_
        period: 24h

storage_config:
  filesystem:
    directory: /loki/storage
  tsdb_shipper:
    active_index_directory: /loki/tsdb-index
    cache_location: /loki/tsdb-cache

compactor:
  working_directory: /loki/compactor
  compaction_interval: 10m
  retention_enabled: true
  retention_delete_delay: 2h
  retention_delete_worker_count: 50

ruler:
  storage:
    type: local
    local:
      directory: /loki/rules
  rule_path: /loki/rules-temp
  alertmanager_url: http://alertmanager:9093
  ring:
    kvstore:
      store: inmemory
  enable_api: true

# OTLP endpoints — both gRPC and HTTP
distributor:
  otlp_config:
    default_resource_attributes_as_index_labels:
      - service.name
      - deployment.environment
      - service.version
      - host.name
```

> **Important:** `distributor.otlp_config.default_resource_attributes_as_index_labels` is what turns the OTel `service.name` into a Loki stream label. Without it, every log line lands in a single stream and filtering by service is impossible.

---

## 12. Tempo configuration — `tempo/tempo.yml`

```yaml
# observability/tempo/tempo.yml
server:
  http_listen_port: 3200

distributor:
  receivers:
    otlp:
      protocols:
        grpc:
          endpoint: 0.0.0.0:4317
        http:
          endpoint: 0.0.0.0:4318

ingester:
  max_block_duration: 5m
  trace_idle_period: 10s
  complete_block_timeout: 1h

compactor:
  compaction:
    block_retention: 168h         # 7 days of traces — bump for prod

storage:
  trace:
    backend: local                # use s3 in prod
    local:
      path: /var/tempo/blocks
    wal:
      path: /var/tempo/wal

# TraceQL metrics generator — powers the Grafana "Traces" panel.
# Correct Tempo 2.x shape: `storage` + `processor` maps, not a list + registry.
metrics_generator:
  storage:
    path: /var/tempo/generator/wal
    remote_write:
      - url: http://prometheus:9090/api/v1/write
        send_timestamps: true
  processor:
    service-graphs: {}
    span-metrics: {}

overrides:
  defaults:
    metrics_generator:
      processors: [service-graphs, span-metrics]

querier:
  max_concurrent_queries: 20

query_frontend:
  search:
    query_timeout: 30s
```

`observability/tempo/overrides.yml` (kept empty for dev — populate per-tenant in prod):

```yaml
# observability/tempo/overrides.yml
overrides: {}
```

---

## 13. Prometheus configuration — `prometheus/prometheus.yml`

```yaml
# observability/prometheus/prometheus.yml
global:
  scrape_interval: 15s
  evaluation_interval: 15s
  external_labels:
    cluster: local
    environment: dev

# The collector pushes to /api/v1/write. Prometheus must run with
# --web.enable-remote-write-receiver for that endpoint to accept data.

rule_files:
  - /etc/prometheus/rules/*.yml

alerting:
  alertmanagers:
    - static_configs:
        - targets: ['alertmanager:9093']

scrape_configs:
  # Collector self-metrics — useful for "is my pipeline backed up?" dashboards
  - job_name: otelcol-agent
    static_configs:
      - targets: ['otel-agent:8888']

  - job_name: otelcol-gateway
    static_configs:
      - targets: ['otel-gateway:8889']

```

### 13.1 Recording rules — `prometheus/rules/service-recording.yml`

**Why recording rules exist:** without them, every dashboard load and every alert evaluation re-computes the same PromQL expression from raw data. `rate(http_server_request_duration_seconds_bucket[5m])` over 100k series is expensive. A recording rule pre-computes that expression every minute and stores the result as a new time series. Dashboards and alerts query the pre-computed series — fast and cheap.

The pattern: define recording rules for the metrics your dashboards and alerts use most (request rate, p95 latency, error rate), then reference those recorded series in alerts and dashboards instead of the raw `http_server_request_duration_seconds_*` metrics.

Pre-aggregate the SLO inputs every minute so dashboards and alerts are cheap:

```yaml
# observability/prometheus/rules/service-recording.yml
groups:
  - name: service-recording
    interval: 1m
    rules:
      # Prometheus label names cannot contain dots: service.name -> service_name,
      # http.route -> http_route, http.status_code -> http_status_code.
      - record: service:http_requests:rate5m
        expr: |
          sum by (service_name, http_route, http_status_code, deployment_environment)
          (rate(http_server_request_duration_seconds_count{synthetic!="true"}[5m]))

      - record: service:http_request_duration:p95_5m
        expr: |
          histogram_quantile(0.95,
            sum by (service_name, http_route, le)
            (rate(http_server_request_duration_seconds_bucket{synthetic!="true"}[5m])))

      - record: service:http_request_duration:p99_5m
        expr: |
          histogram_quantile(0.99,
            sum by (service_name, http_route, le)
            (rate(http_server_request_duration_seconds_bucket{synthetic!="true"}[5m])))

      - record: service:http_errors:rate5m
        expr: |
          sum by (service_name, deployment_environment)
          (rate(http_server_request_duration_seconds_count{synthetic!="true",http_status_code=~"5.."}[5m]))

      - record: service:wolverine_processed:rate5m
        expr: |
          sum by (service_name)
          (rate(wolverine_messages_received_total[5m]))
```

> `wolverine_messages_received_total` is an example. Use the actual instrument names exposed by your Wolverine version; the meter is `Wolverine:{ApplicationName}`.

### 13.2 Alerting rules — `prometheus/rules/service-alerting.yml`

```yaml
# observability/prometheus/rules/service-alerting.yml
groups:
  - name: service-alerting
    interval: 30s
    rules:
      - alert: HighErrorRate
        expr: |
          (
            sum by (service_name, deployment_environment)
            (rate(http_server_request_duration_seconds_count{synthetic!="true",http_status_code=~"5.."}[5m]))
            /
            clamp_min(
              sum by (service_name, deployment_environment)
              (rate(http_server_request_duration_seconds_count{synthetic!="true"}[5m])), 1e-9)
          ) > 0.01
        for: 5m
        labels:
          severity: page
          slo: availability-99.9
        annotations:
          summary: "{{ $labels.service_name }} error rate > 1%"
          description: "{{ $labels.service_name }} in {{ $labels.deployment_environment }} is returning 5xx for {{ $value | humanizePercentage }} of requests over 5m."

      - alert: HighLatencyP95
        expr: service:http_request_duration:p95_5m > 0.5
        for: 10m
        labels:
          severity: ticket
          slo: latency-p95-500ms
        annotations:
          summary: "{{ $labels.service_name }} p95 latency > 500ms"
          description: "p95 = {{ $value | humanizeDuration }} on route {{ $labels.http_route }}"

      - alert: NoTelemetryFromCollector
        expr: up{job="otel-collector"} == 0
        for: 5m
        labels:
          severity: page
        annotations:
          summary: "The OpenTelemetry collector is not being scraped"
```

> Alerts on application metrics require the collector's `prometheus` exporter (dev) or `prometheusremotewrite` (prod). There is no direct scrape of the .NET apps in the prod topology, so `up{job="myapp-*"}` is not a valid liveness signal there.

## 14. Alertmanager — `alertmanager/alertmanager.yml`

```yaml
# observability/alertmanager/alertmanager.yml
global:
  resolve_timeout: 5m

route:
  group_by: ['alertname', 'service_name', 'deployment_environment']
  group_wait: 30s
  group_interval: 5m
  repeat_interval: 4h
  receiver: dev-null   # default — overridden per severity
  routes:
    - matchers:
        - severity = page
      receiver: oncall-pager
      group_wait: 10s
      repeat_interval: 1h
    - matchers:
        - severity = ticket
      receiver: ticket-queue
      repeat_interval: 12h

receivers:
  - name: dev-null
    # A receiver with no configs is a valid no-op.

  - name: oncall-pager
    # Replace with PagerDuty / Opsgenie webhook in prod
    webhook_configs:
      - url: http://localhost:5001/alert
        send_resolved: true
  - name: ticket-queue
    # Replace with Jira / Linear webhook in prod
    webhook_configs:
      - url: http://localhost:5002/alert
        send_resolved: true

inhibit_rules:
  # If a host is down, suppress high-latency / error alerts from that host
  - source_matchers: [alertname = "NoTelemetryFromCollector"]
    target_matchers: [severity = "page"]
    equal: ['service_name']
```

> **Synthetic route required:** before this Alertmanager config can handle alerts from the synthetic suite, add the synthetic-specific routes described in §21.9 (smoke-test alerts → `smoke-receiver`; other synthetic alerts → `oncall-pager`). Without those routes, the hourly `SyntheticAlertmanagerSmokeTest` alert will page on-call every hour.

---

## 15. Grafana provisioning

### 15.1 Datasources — `grafana/provisioning/datasources/datasources.yml`

```yaml
# observability/grafana/provisioning/datasources/datasources.yml
apiVersion: 1

datasources:

  - name: Prometheus
    type: prometheus
    access: proxy
    url: http://prometheus:9090
    isDefault: true
    jsonData:
      httpMethod: POST
      exemplarTraceIdDestinations:
        - name: trace_id
          datasourceUid: tempo

  - name: Loki
    type: loki
    access: proxy
    url: http://loki:3100
    jsonData:
      derivedFields:
        # Pull trace_id out of structured logs and link to Tempo
        - datasourceUid: tempo
          name: TraceID
          matcherRegex: '"trace_id":"(\w+)"'
          url: '$${__value.raw}'
          urlDisplayLabel: 'View trace in Tempo'

  - name: Tempo
    type: tempo
    uid: tempo
    access: proxy
    url: http://tempo:3200
    jsonData:
      tracesToLogsV2:
        # When looking at a trace, show matching Loki logs
        datasourceUid: loki
        filterByTraceID: true
        tags: ['service.name']
      tracesToMetrics:
        datasourceUid: prometheus
      serviceMap:
        datasourceUid: prometheus
      nodeGraph:
        enabled: true
      lokiSearch:
        datasourceUid: loki

  - name: Alertmanager
    type: alertmanager
    access: proxy
    url: http://alertmanager:9093
    jsonData:
      implementation: prometheus
      handleGrafanaManagedAlerts: false
```

### 15.2 Dashboards loader — `grafana/provisioning/dashboards/dashboards.yml`

```yaml
# observability/grafana/provisioning/dashboards/dashboards.yml
apiVersion: 1
providers:
  - name: 'service-dashboards'
    orgId: 1
    folder: 'Services'
    type: file
    disableDeletion: false
    updateIntervalSeconds: 30
    options:
      path: /etc/grafana/provisioning/dashboards
```

### 15.3 `grafana/grafana.ini` (minimal — most settings via env)

```ini
[paths]
provisioning = /etc/grafana/provisioning

[server]
domain = localhost
http_port = 3000

[auth.anonymous]
enabled = false

[users]
allow_sign_up = false
auto_assign_org = true
auto_assign_org_role = Viewer

[feature_toggles]
enable = tempoSearch tempoBackendSearch traceqlEditor metricsSummary lokiDataplan
```

### 15.4 The one canonical service dashboard

Drop a JSON file at `grafana/provisioning/dashboards/service-overview.json`. The full JSON is ~600 lines — the most useful panels are:

| Panel | Query |
|---|---|
| Request rate | `service:http_requests:rate5m{service_name="$service"}` |
| p50 / p95 / p99 | `service:http_request_duration:p95_5m{service_name="$service"}` (one per quantile) |
| Error rate | `service:http_errors:rate5m{service_name="$service"}` |
| Active requests | `http_server_active_requests{service_name="$service"}` |
| GC pause time | `process_runtime_dotnet_gc_pause_time_seconds_sum` |
| Thread pool queue | `process_runtime_dotnet_thread_pool_queue_length` |
| Wolverine throughput | `service:wolverine_processed:rate5m{service_name="$service"}` |
| Recent traces ( Tempo panel ) | Tempo datasource → service query |
| Recent logs ( Loki panel ) | Loki datasource → `{service_name="$service"} \| json` |

Use the Grafana **service template variable** pattern so one dashboard serves all five services.

```json
{
  "templating": {
    "list": [
      {
        "name": "service",
        "type": "query",
        "datasource": "Prometheus",
        "query": "label_values(http_server_request_duration_seconds_count, service_name)",
        "refresh": 2
      },
      {
        "name": "env",
        "type": "query",
        "datasource": "Prometheus",
        "query": "label_values(http_server_request_duration_seconds_count, deployment_environment)",
        "refresh": 2
      }
    ]
  }
}
```

A starter JSON you can save as `service-overview.json` and refine:

```json
{
  "title": "Service Overview",
  "schemaVersion": 39,
  "tags": ["myapp", "service"],
  "templating": {
    "list": [
      { "name": "service", "type": "query", "datasource": { "uid": "Prometheus" },
        "query": "label_values(http_server_request_duration_seconds_count, service_name)", "refresh": 2 },
      { "name": "env", "type": "query", "datasource": { "uid": "Prometheus" },
        "query": "label_values(http_server_request_duration_seconds_count, deployment_environment)", "refresh": 2 }
    ]
  },
  "panels": [
    {
      "title": "Request rate (rps)",
      "type": "timeseries",
      "datasource": { "uid": "Prometheus" },
      "targets": [{
        "expr": "sum by (http_route) (service:http_requests:rate5m{service_name=\"$service\",deployment_environment=\"$env\"})",
        "legendFormat": "{{http_route}}"
      }]
    },
    {
      "title": "p95 latency by route",
      "type": "timeseries",
      "datasource": { "uid": "Prometheus" },
      "targets": [{
        "expr": "service:http_request_duration:p95_5m{service_name=\"$service\",deployment_environment=\"$env\"}",
        "legendFormat": "{{http_route}}"
      }]
    },
    {
      "title": "Error rate",
      "type": "stat",
      "datasource": { "uid": "Prometheus" },
      "targets": [{
        "expr": "sum(service:http_errors:rate5m{service_name=\"$service\",deployment_environment=\"$env\"}) / sum(service:http_requests:rate5m{service_name=\"$service\",deployment_environment=\"$env\"})",
        "unit": "percentunit"
      }]
    }
  ]
}
```

---

## 16. SLOs — definition + budget

**What is an SLO and why do you need one?**

An SLO (Service Level Objective) is a **contract with yourself** about how reliable your service should be. It has three parts:
- A **target** ("99.9% of requests succeed")
- A **window** ("over 30 days")
- A **measurement** ("success = HTTP status < 500")

Without an SLO, you're alerting on gut feel — "error rate seems high today." With an SLO, you have a mathematical definition of "good enough" and an **error budget** that tells you exactly when to page someone.

**What is an error budget?**

If your SLO is 99.9% availability over 30 days, your error budget is 0.1% × 30 days = **43.2 minutes of allowed downtime per month**. As long as you've had less than 43 minutes of errors in the last 30 days, you're within budget — no page. The moment you exceed it, you're burning budget faster than you can afford, and you should be alerted.

**Why multi-window burn rate?**

A single threshold ("error rate > 1% for 5 minutes → page") is either too noisy (pages on every blip) or too slow (doesn't page until the damage is done). Multi-window burn rate solves this by checking two windows simultaneously:

- **Short window** (5 minutes) — catches acute outages fast. A 14.4× burn rate here means you're using 2% of your monthly budget in one hour.
- **Long window** (1 hour) — confirms it's not a fluke. Same threshold in both windows means it's real.

If both windows are burning fast → **page** (acute incident). If only the long window is burning → **ticket** (chronic degradation). This gives you one alerting rule that handles both "the site is down RIGHT NOW" and "we're slowly going to miss our SLO this month."

SLOs are not dashboards; they are contracts. Record each service's SLI, target, window, and error-budget policy next to that service (for example `src/Services/MyApp.ApiHost/SLO.md`). Do not treat the formulas below as an operating model until that policy exists.

| Service | SLO | Window | Burn budget |
|---|---|---|---|
| `myapp.apihost` | 99.9% of `2xx/3xx/4xx` over 30d | 30d | 43m error budget per 30d |
| `myapp.apihost` | p95 < 500ms over 30d | 30d | 14m slow budget per 30d |
| `myapp.ordersapi` | 99.5% message processing success over 7d | 7d | 50m DLQ budget per 7d |
| `myapp.gateway` | 99.95% availability over 30d | 30d | 22m error budget per 30d |

A "multi-window multi-burn-rate" alert (short + long window) is the right pattern — when both windows burn fast, page. When only the long window burns, ticket. Sketch:

```yaml
# 5m × 1h multi-window burn for 99.9% availability
- alert: SLOAvailabilityBurnRateFast
  expr: |
    (
      sum by (service_name) (rate(http_server_request_duration_seconds_count{http_status_code=~"5.."}[5m]))
      /
      clamp_min(sum by (service_name) (rate(http_server_request_duration_seconds_count[5m])), 1e-9)
    ) > 0.0144   # 14.4x burn → uses 2% of monthly budget in 1h
    and
    (
      sum by (service_name) (rate(http_server_request_duration_seconds_count{http_status_code=~"5.."}[1h]))
      /
      clamp_min(sum by (service_name) (rate(http_server_request_duration_seconds_count[1h])), 1e-9)
    ) > 0.0144
  for: 2m
  labels: { severity: page, slo: availability-99.9 }
```

Use the same shape for the latency SLO (replace the numerator with `service:http_request_duration:p95_5m > 0.5`).

---

## 17. Dev workflow

### 17.1 Bring the stack up (local dev)

The dev stack is one file at the repo root. It includes RabbitMQ, Elasticsearch, the observability stack, and all five .NET hosts:

```bash
# From MyApp/
docker compose -f docker-compose.dev.yml up -d --build

# Verify
docker compose -f docker-compose.dev.yml ps
curl -s http://localhost:9090/-/healthy   # Prometheus
curl -s http://localhost:3100/ready       # Loki
curl -s http://localhost:3200/ready       # Tempo
open http://localhost:3000                # Grafana (admin/admin)
```

The production topology in §5–§6 uses the separate `observability/` compose file with agent + gateway. Do not run both stacks at once; they use the same host ports.

### 17.2 Run one host locally (debugging)

Stop the compose host first, or it will hold the port and the RabbitMQ consumer:

```bash
# From MyApp/
docker compose -f docker-compose.dev.yml stop myapp-apihost
```

Then:

```bash
cd src/Services/MyApp.ApiHost
dotnet run
# Env is loaded by DotNetEnv:
#   OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
#   OTEL_DEPLOYMENT_ENVIRONMENT=dev
```

Hit a few endpoints to generate telemetry:

```bash
curl -s http://localhost:5101/health
TOKEN=$(curl -s -X POST http://localhost:5101/auth/login \
  -H 'Content-Type: application/json' \
  -d '{"Email":"admin@example.com","Password":"ChangeMe-12345"}' \
  | jq -r .access_token)
curl -s -H "Authorization: Bearer $TOKEN" "http://localhost:5101/products?page=1&pageSize=5&desc=false"
curl -s -H "Authorization: Bearer $TOKEN" -X POST http://localhost:5101/products \
  -H 'Content-Type: application/json' \
  -d '{"Name":"Telemetry Test Product","Category":"Books","Price":19.99,"Stock":10}'
```

### 17.3 Verify signals arrive

```bash
# Traces in Tempo?
curl -s "http://localhost:3200/api/search?tags=service.name=myapp.apihost&limit=5" | jq

# Metrics in Prometheus?
curl -s "http://localhost:9090/api/v1/query?query=http_server_request_duration_seconds_count" | jq '.data.result | length'

# Logs in Loki?
curl -s -G "http://localhost:3100/loki/api/v1/query_range" \
  --data-urlencode 'query={service_name="myapp.apihost"}' \
  --data-urlencode 'limit=5' | jq '.data.result'
```

### 17.4 Iterating on configs

Collector configs hot-reload by default (file watcher). For Prometheus and Loki, send `SIGHUP`:

```bash
docker exec prometheus kill -HUP 1
docker exec loki       kill -HUP 1
```

Grafana re-reads provisioned dashboards every 30s (set in `dashboards.yml`).

### 17.5 Turning the noise down

In dev you'll get a lot of `/health` traffic and Wolverine retries. Two options:

1. Filter at source (in `AddAspNetCoreInstrumentation`'s `Filter` — we already do this).
2. Filter at the collector agent (`filter/drop_healthchecks` processor — already in `agent.yaml`).

Don't filter in the gateway for dev-only noise — that's the agent's job.

### 17.6 Verify with the synthetic probe

After your first deploy of the synthetic suite (§21), one command verifies the whole pipeline end-to-end:

```bash
python3 scripts/telemetry-health-check.py \
  --hosts http://localhost:5101 \
  --probe-token $PROBE_TOKEN \
  --json | jq '.reports[].stages[] | {stage, passed, detail}'
```

Expected: all six stages (`probe`, `prometheus`, `loki`, `tempo`, `grafana`, `alertmanager`) pass within the 30s timeout per stage. If `alertmanager` fails with "smoke receiver unreachable", the hourly smoke alert hasn't fired yet — wait an hour or temporarily lower the `synthetic:smoke:hour` interval in §21.8 for dev.

Wire this into your dev workflow as a one-shot after `docker compose up` so you catch pipeline breakage before iterating on app code.

---

## 18. Production deployment (Kubernetes)

The same configs lift into a cluster with three changes:

### 18.1 Collector topology

| Collector | Mode | Purpose |
|---|---|---|
| `otel-agent` | **DaemonSet** | One per node; apps push to `OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317` (node-local) |
| `otel-gateway` | **Deployment** (HPA) | Cluster-level; receives from agents, does tail sampling + fan-out |

DaemonSet manifest (sketch):

```yaml
apiVersion: apps/v1
kind: DaemonSet
metadata:
  name: otel-agent
  namespace: observability
spec:
  selector: { matchLabels: { app: otel-agent } }
  template:
    metadata: { labels: { app: otel-agent } }
    spec:
      containers:
        - name: otelcol
          image: otel/opentelemetry-collector-contrib:0.110.0
          args: ["--config=/etc/otelcol/agent.yaml"]
          ports:
            - { containerPort: 4317, name: otlp-grpc }
            - { containerPort: 4318, name: otlp-http }
          env:
            - { name: DEPLOYMENT_ENV, value: "prod" }
            - { name: K8S_CLUSTER, value: "myapp-prod" }
            - { name: CLOUD_PROVIDER, value: "aws" }
            - { name: CLOUD_REGION, value: "eu-central-1" }
          volumeMounts:
            - { name: config, mountPath: /etc/otelcol/agent.yaml, subPath: agent.yaml }
            - { name: storage, mountPath: /var/lib/otelcol/storage }
      volumes:
        - name: config
          configMap: { name: otel-agent-config }
        - name: storage
          emptyDir: {}
```

Gateway Deployment with HPA on CPU + queue length:

```yaml
apiVersion: apps/v1
kind: Deployment
metadata: { name: otel-gateway, namespace: observability }
spec:
  replicas: 3
  selector: { matchLabels: { app: otel-gateway } }
  template:
    metadata: { labels: { app: otel-gateway } }
    spec:
      containers:
        - name: otelcol
          image: otel/opentelemetry-collector-contrib:0.110.0
          args: ["--config=/etc/otelcol/gateway.yaml"]
          ports:
            - { containerPort: 4317, name: otlp-grpc }
            - { containerPort: 8889, name: internal-metrics }
          env:
            - { name: K8S_CLUSTER, value: "myapp-prod" }
          volumeMounts:
            - { name: config, mountPath: /etc/otelcol/gateway.yaml, subPath: gateway.yaml }
      volumes:
        - name: config
          configMap: { name: otel-gateway-config }
---
apiVersion: autoscaling/v2
kind: HorizontalPodAutoscaler
metadata: { name: otel-gateway, namespace: observability }
spec:
  scaleTargetRef: { apiVersion: apps/v1, kind: Deployment, name: otel-gateway }
  minReplicas: 3
  maxReplicas: 12
  metrics:
    - type: Resource
      resource: { name: cpu, target: { type: Utilization, averageUtilization: 70 } }
    - type: Pods
      pods:
        metric: { name: otelcol_queue_size }
        target: { type: AverageValue, averageValue: "5000" }
```

### 18.2 Backend storage

Replace local FS with object storage:

```yaml
# Tempo
storage:
  trace:
    backend: s3
    s3:
      bucket: myapp-tempo-traces
      endpoint: s3.eu-central-1.amazonaws.com

# Loki (Loki 3.2 syntax)
common:
  storage:
    s3:
      bucket_name: myapp-loki-logs
      endpoint: s3.eu-central-1.amazonaws.com
```

Prometheus stays local (or use Mimir / Thanos for HA).

### 18.3 Retention

| Signal | Dev | Prod |
|---|---|---|
| Metrics | 15d | 90d (or roll up to Mimir) |
| Logs | 30d | 30d hot, 1y in cold archive (S3 lifecycle) |
| Traces | 7d | 7d full + 30d sampled (rely on span-metrics for long history) |

### 18.4 Security checklist

- [ ] Collector → backends uses TLS (replace `tls.insecure: true`)
- [ ] OTLP app → collector uses mTLS (use a sidecar with envoy or `tls.settings.cert_file`)
- [ ] Loki has `auth_enabled: true` + OIDC in front (Grafana proxy)
- [ ] Tempo query endpoint behind Grafana's auth proxy (don't expose :3200)
- [ ] Alertmanager webhook receivers use HTTPS + shared secret
- [ ] PII: never put user IDs in trace attributes; use `myapp.tenant.id` instead. The `MyEnvironmentLogEnricher` only adds non-PII.
- [ ] EF Core instrumentation attributes verified: the 1.13-beta package does not expose `SetDbStatementForText`, so confirm what `db.*` attributes are emitted.

---

## 19. Usage cookbook

### 19.1 Find a slow request in Grafana

1. Open Grafana → **Explore** → Tempo.
2. TraceQL: `{ resource.service.name = "myapp.apihost" && span.http.status_code = 200 } | duration > 1s`
3. Sort by duration, click the slowest span.
4. Right panel → **Logs for this trace** (Loki) shows every log line tied to that `trace_id`.
5. Right panel → **Metrics for this trace** shows the span-metrics time series.

### 19.2 Pivot from a metric spike to a trace

1. Grafana dashboard → Error Rate panel → click the spike.
2. "View exemplars" — each exemplar carries a `trace_id`.
3. Click the exemplar → jumps to Tempo with the trace pre-loaded.

> Requires `enable-feature=exemplar-storage` on Prometheus (set in §5.3) and `exemplarTraceIdDestinations` in the Prometheus datasource (set in §15.1).

### 19.3 Pivot from a log line to a trace

1. Grafana → Explore → Loki.
2. Query: `{service_name="myapp.apihost"} |= "ERROR" | json`
3. Any log row with a `trace_id` shows a **"View trace in Tempo"** link (set up in §15.1 `derivedFields`).
4. Click → Tempo opens with the full trace.

### 19.4 LogQL cookbook

```logql
# All errors from a service in the last 5m, parsed
{service_name="myapp.apihost", level="Error"} | json | line_format "{{.message}} [{{.trace_id}}]"

# Group logs by HTTP route
sum by (http_route) (
  rate({service_name="myapp.apihost"} | json | __error__ = "" [5m])
)

# Find logs for a specific trace
{service_name="myapp.apihost"} | json | trace_id = "0a65cfdb..."

# Errors correlated with 5xx HTTP responses
{service_name="myapp.apihost", level="Error"} | json | http_status_code =~ "5.."
```

### 19.5 TraceQL cookbook

```traceql
# All slow POSTs to ApiHost
{ resource.service.name = "myapp.apihost"
  && span.http.method = "POST"
  && span.http.status_code = 201
} | duration > 500ms

# All Wolverine consumer spans that errored
{ resource.service.name = "myapp.ordersapi"
  && span.wolverine.message_type = "ProductCreatedIntegrationEvent"
  && status = error
}

# Find every span where a specific aggregate ID was touched
{ span."myapp.aggregate.id" = "0a65cfdb-d762-1503-a240-8ebb602ae959" }
```

### 19.6 PromQL cookbook

```promql
# p95 latency per service over the last 5m
histogram_quantile(0.95,
  sum by (service_name, le) (rate(http_server_request_duration_seconds_bucket[5m])))

# Error budget burn rate (99.9% SLO)
1 - (
  sum(rate(http_server_request_duration_seconds_count{http_status_code!~"5.."}[1h]))
  /
  sum(rate(http_server_request_duration_seconds_count[1h]))
)

# Wolverine message throughput (instrument names are version-specific; adjust to the series you see)
sum by (service_name, wolverine_message_type) (
  rate(wolverine_messages_received_total[5m])
)

# Top 5 slowest routes by p99 over 1h
topk(5,
  histogram_quantile(0.99,
    sum by (service_name, http_route, le) (
      rate(http_server_request_duration_seconds_bucket[1h])
    )
  )
)
```

### 19.7 Trace / log correlation — how to verify it works

Generate a request, grab the trace_id from Tempo, then verify Loki has the same trace_id:

```bash
# Generate
curl -s -H "Authorization: Bearer $TOKEN" -X POST http://localhost:5101/products \
  -H 'Content-Type: application/json' \
  -d '{"Name":"Correlation Check","Category":"Books","Price":12.34,"Stock":1}'

# Find the trace in Tempo
curl -s "http://localhost:3200/api/search?q=%7Bresource.service.name%3D%22myapp.apihost%22%7D&limit=1" \
  | jq -r '.traces[0].traceID'

# Now grep Loki for that trace_id
TRACE_ID=<paste>
curl -s -G "http://localhost:3100/loki/api/v1/query_range" \
  --data-urlencode "query={service_name=\"myapp.apihost\"} | json | trace_id=\"$TRACE_ID\"" \
  | jq '.data.result | length'   # should be > 0
```

If zero: check that `IncludeFormattedMessage` / `IncludeScopes` are set on the OTel logger provider (they are in §7.1), and that `distributor.otlp_config` isn't filtering out `trace_id` (it's in `structured_metadata`, not `index_label` — correct).

---

## 20. Testing telemetry

You don't need a live backend to test instrumentation. Add a unit test using the in-memory exporter:

```xml
<!-- tests/MyApp.Tests.Unit/MyApp.Tests.Unit.csproj -->
<PackageReference Include="OpenTelemetry.Exporter.InMemory" />
```

```csharp
// tests/MyApp.Tests.Unit/Telemetry/CreateProductHandlerTests.cs
public class CreateProductHandlerTests
{
    [Fact]
    public async Task Create_product_emits_metrics_and_span()
    {
        // Arrange — in-memory exporters so we can assert without a collector
        var exportedMetrics = new List<Metric>();
        var exportedSpans   = new List<Activity>();

        var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter("MyApp.Catalog")
            .AddInMemoryExporter(exportedMetrics)
            .Build();

        var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource("MyApp.Catalog")
            .AddInMemoryExporter(exportedSpans)
            .Build();

        var db = TestDb.CreateInMemory<CatalogDbContext>();
        var handler = new CreateProductCommandHandler(db);

        // Act
        await handler.Handle(
            new CreateProductCommand("Test", "Books", 19.99m, 10),
            CancellationToken.None);

        meterProvider.Dispose();
        tracerProvider.Dispose();

        // Assert
        exportedSpans.Should().ContainSingle(s => s.DisplayName == "CreateProduct");
        exportedMetrics.Should().Contain(m =>
            m.Name == "myapp.catalog.products_created");
    }
}
```

For integration tests (the kit already has `MyApp.Tests.Integration`), point `OTEL_EXPORTER_OTLP_ENDPOINT` at a containerized collector started in `WebApplicationFactory` setup.

---

## 21. Observability Health Test Suite — synthetic E2E validation

### 21.1 Why you need it

Telemetry pipelines fail **silently**. Each layer can break independently while the rest of the system keeps working — until someone tries to debug an incident and finds the dashboards empty for the last 6 hours. A unit test with in-memory exporters (§20) proves the .NET side emits the right signals; it does **not** prove that the collector received them, that Prometheus ingested them, that Loki indexed them, that Alertmanager can route them, or that Grafana can query them.

Common silent failures the suite catches:

| Failure | Symptom | How the suite catches it |
|---|---|---|
| Collector queue full → dropping data | `otelcol_exporter_send_failed_*` rises, dashboards thin out | Probe's metric never appears in Prometheus within 30s |
| Loki schema upgrade / config drift | New logs land in default stream, log queries return 0 hits | Probe's LogQL query returns 0 hits |
| Tempo retention or backend changed | Old traces gone, new traces not arriving | Probe's TraceQL query times out |
| Alertmanager webhook auth rotated | Real alerts silently dropped at the receiver | Smoke alert never arrives at the smoke receiver |
| Grafana datasource token expired | Dashboards broken, no one notices for hours | Datasource `/health` endpoint returns non-OK |
| Network policy blocks OTLP port | App can't reach collector; `OtelExporterDroppingData` alert fires but no one's watching the alerts either | Probe endpoint returns 500, runner emits fail metric → `SyntheticProbeMissing` pages |
| OTel SDK removed from a host during a refactor | Host silently stops emitting; dashboards for that host empty | `SyntheticProbeMissing{service_name=…}` alerts within 10m |

The suite turns each of these from "discover during the next incident" into "discover within 10 minutes, before anyone is paged for the underlying incident."

### 21.2 Telemetry test pyramid

| Layer | What | Where | When |
|---|---|---|---|
| **Unit** | In-memory exporter assertions per handler (§20) | `tests/MyApp.Tests.Unit` | CI, every commit |
| **Integration** | `WebApplicationFactory` + containerized collector + in-memory Prometheus | `tests/MyApp.Tests.Integration` | CI, every PR |
| **E2E synthetic** | Probe endpoint + runner polling real backends | K8s CronJob / docker-compose service | Every 5 min in prod, every 15 min in dev |
| **Synthetic alert** | Periodic alert fired through Alertmanager to a smoke-test webhook | Prometheus rule | Every 1h |

The first two layers are gated by code review. The last two run continuously in every environment and become **dashboards + alerts themselves** — a "no probe in 10 minutes" alert is one of the highest-leverage alerts you'll ever write, because it catches the entire class of "telemetry is broken" before it compounds with a real incident.

### 21.3 Architecture

```
  ┌──────────────────────────────────────────────────────────────┐
  │  Probe runner  (K8s CronJob / compose service, every 5 min)   │
  └────────────────┬─────────────────────────────────────────────┘
                   │ POST /health/telemetry  (Bearer probe-token)
                   ▼
  ┌──────────────────────────────────────────────────────────────┐
  │  Each .NET host (ApiHost, OrdersApi, InvoicesApi, …)         │
  │  /health/telemetry endpoint                                  │
  │   • increments synthetic.probe.runs (per service, no probe id) │
  │   • logs a structured line carrying trace_id                  │
  │   • starts a synthetic.health_probe Activity                 │
  │   • returns { probeId, traceId, timestamp }                  │
  └────────────────┬─────────────────────────────────────────────┘
                   │ OTLP  (same pipeline as prod traffic)
                   ▼
  ┌──────────────────────────────────────────────────────────────┐
  │  Collector (agent → gateway) → Prometheus / Loki / Tempo      │
  └────────────────┬─────────────────────────────────────────────┘
                   │
  ┌────────────────┴─────────────────────────────────────────────┐
  │  Runner polls each backend with probe_id / trace_id          │
  │   • PromQL:  increase(synthetic_probe_runs_total[2m]) > 0    │
  │   • LogQL:   {service_name=~"myapp.*"} |= "probe_id=…"       │
  │   • TraceQL: { span.probe.id = "…" }                          │
  │   • Grafana: GET /api/datasources/uid/{uid}/health            │
  └────────────────┬─────────────────────────────────────────────┘
                   │
  ┌────────────────┴─────────────────────────────────────────────┐
  │  Runner pushes synthetic_health_check_result to Pushgateway   │
  │  → absent_over_time(...) for 15m → SyntheticProbeRunnerDown   │
  └──────────────────────────────────────────────────────────────┘

  ┌──────────────────────────────────────────────────────────────┐
  │  Prometheus rule fires SyntheticAlertmanagerSmokeTest hourly  │
  │  → Alertmanager → smoke-receiver webhook                      │
  │  → receiver writes last_receipt file + exposes /metrics       │
  │  → (time() - smoke_receiver_last_receipt_epoch) > 7200        │
  │     → SyntheticAlertmanagerSmokeMissing pages on-call         │
  └──────────────────────────────────────────────────────────────┘
```

### 21.4 Probe endpoint — `Features/Diagnostics/TelemetryProbeEndpoint.cs`

The kit already has `Features/Diagnostics/ErrorDiagnosticsEndpoint.cs` for dev-only diagnostics. The telemetry probe is **production-available** (gated by a probe token, not by environment) because its whole point is to validate prod. Place it under `Features/Diagnostics/` next to the existing diagnostics endpoint.

```csharp
// src/Services/MyApp.ApiHost/Features/Diagnostics/TelemetryProbeEndpoint.cs
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;

namespace MyApp.Features.Diagnostics;

public sealed class TelemetryProbeEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/health/telemetry", HandleAsync)
            .WithName("TelemetryProbe")
            .WithTags("Diagnostics")
            .ExcludeFromDescription()                 // not in OpenAPI
            .RequireAuthorization("ProbePolicy");      // see §21.11
    }

    private static IResult HandleAsync(
        HttpContext http,
        IConfiguration cfg,
        ILogger<TelemetryProbeEndpoint> logger,
        CancellationToken ct)
    {
        // The Activity.Current.TraceId is the W3C trace id of the incoming
        // request — that's what every backend will index. Returning it lets
        // the runner grep Loki / Tempo for the same id.
        var traceId = Activity.Current?.TraceId.ToHexString()
                      ?? Guid.NewGuid().ToString("N");
        var probeId = Guid.NewGuid().ToString("N");
        var serviceName = cfg["Probe:ServiceName"]
                          ?? cfg["OTEL_SERVICE_NAME"]
                          ?? "unknown";
        var now = DateTimeOffset.UtcNow;

        // 1) Synthetic span — every probe is one span so Tempo always has
        //    something to find.
        using var activity = TelemetryProbeActivitySource.Instance
            .StartActivity("synthetic.health_probe");
        activity?.SetTag("synthetic", "true");
        activity?.SetTag("probe.id", probeId);
        activity?.SetTag("probe.source", "telemetry-health-suite");
        activity?.SetTag("probe.trace_id", traceId);

        // 2) Synthetic metric — deliberately NOT labelled with probe.id.
        //    A per-probe label creates a new Prometheus series every run and
        //    never stops growing. The probe id stays only in the log line and
        //    span tag, where it is used for exact trace/log correlation.
        // service.name comes from the resource, not from a duplicate metric label.
        TelemetryProbeMetrics.ProbeRuns.Add(1,
            new KeyValuePair<string, object?>("synthetic", "true"));

        // 3) Synthetic log — must include trace_id so the Loki → Tempo link works.
        logger.LogInformation(
            "Synthetic telemetry probe fired. probe_id={ProbeId} trace_id={TraceId} service={Service}",
            probeId, traceId, serviceName);

        return TypedResults.Ok(new
        {
            probeId,
            traceId,
            service = serviceName,
            timestamp = now,
            signals = new[] { "metric", "log", "trace" },
        });
    }
}

internal static class TelemetryProbeActivitySource
{
    public static readonly ActivitySource Instance = new("MyApp.Synthetic", "1.0.0");
}

internal static class TelemetryProbeMetrics
{
    public static readonly Meter Meter = new("MyApp.Synthetic", "1.0.0");

    public static readonly Counter<long> ProbeRuns =
        Meter.CreateCounter<long>("synthetic.probe.runs");
}
```

Register the synthetic source and meter explicitly in the same place as the other sources and meters:

```csharp
otel.WithTracing(t => t
    .AddSource("Wolverine")
    .AddSource("MyApp.Catalog")
    .AddSource("MyApp.Orders")
    .AddSource("MyApp.Search")
    .AddSource("MyApp.Invoices")
    .AddSource("MyApp.Shipping")
    .AddSource("MyApp.Synthetic")
    // ... instrumentation
    );

otel.WithMetrics(m => m
    .AddMeter("Wolverine*")
    .AddMeter("MyApp.Catalog")
    .AddMeter("MyApp.Orders")
    .AddMeter("MyApp.Search")
    .AddMeter("MyApp.Invoices")
    .AddMeter("MyApp.Shipping")
    .AddMeter("MyApp.Synthetic")
    // ... instrumentation
    );
```

> **Why a separate `MyApp.Synthetic` source/meter name?** So you can drop the synthetic source from a non-prod tracer provider, or filter `synthetic="true"` out of SLO recording rules (§21.10), without touching the real instrumentation. Keep synthetic signals namespaced distinctly.

### 21.5 Probe runner — `scripts/telemetry-health-check.py`

A standalone Python script (stdlib only — no `requests`, no `prometheus-client`) that calls each host, then polls each backend. Designed to run as a K8s CronJob (§21.6) or as a docker-compose service for dev.

```python
#!/usr/bin/env python3
"""
Observability Health Test Suite — synthetic E2E validation.

Calls each host's /health/telemetry endpoint, then polls Prometheus, Loki,
Tempo, and Grafana to verify the telemetry actually arrived. Emits its own
metric (synthetic_health_check_result) via Pushgateway so a missing-result
alert fires when the runner itself is broken.

Exit codes:
  0 = all stages passed
  1 = one or more stages failed
  2 = could not reach a host
"""
from __future__ import annotations
import argparse, json, os, sys, time, urllib.request, urllib.parse, urllib.error
from dataclasses import dataclass, field, asdict
from typing import Optional

@dataclass
class StageResult:
    stage: str
    target: str
    passed: bool
    detail: str
    elapsed_ms: int

@dataclass
class ProbeReport:
    host: str
    service: str
    trace_id: str
    probe_id: str
    stages: list[StageResult] = field(default_factory=list)

    @property
    def all_passed(self) -> bool:
        return all(s.passed for s in self.stages)


# ─── HTTP helpers ────────────────────────────────────────────────────────
def http_get(url: str, timeout: int = 5):
    req = urllib.request.Request(url, method="GET")
    try:
        with urllib.request.urlopen(req, timeout=timeout) as resp:
            body = resp.read().decode("utf-8", errors="replace")
            try:
                return resp.status, json.loads(body)
            except json.JSONDecodeError:
                return resp.status, body
    except urllib.error.URLError as e:
        return 0, str(e)

def http_post(url: str, body: bytes, headers: dict, timeout: int = 5):
    req = urllib.request.Request(url, data=body, headers=headers, method="POST")
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.status, json.loads(resp.read())


# ─── Stages ──────────────────────────────────────────────────────────────
def call_probe(host_url: str, token: str) -> ProbeReport:
    """Stage 1: call /health/telemetry on a host."""
    start = time.time()
    try:
        status, body = http_post(
            f"{host_url}/health/telemetry",
            b"{}",
            {"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
        )
    except urllib.error.URLError as e:
        return ProbeReport(
            host=host_url, service="", trace_id="", probe_id="",
            stages=[StageResult("probe", host_url, False, f"unreachable: {e}",
                                int((time.time() - start) * 1000))])
    elapsed = int((time.time() - start) * 1000)
    if status != 200:
        return ProbeReport(
            host=host_url, service="", trace_id="", probe_id="",
            stages=[StageResult("probe", host_url, False, f"HTTP {status}", elapsed)])
    return ProbeReport(
        host=host_url, service=body["service"], trace_id=body["traceId"], probe_id=body["probeId"],
        stages=[StageResult("probe", host_url, True, "200 OK", elapsed)])

def wait_for_prometheus(service: str, prom_url: str, timeout_s: int = 30) -> StageResult:
    """Stage 2: query Prometheus for any recent synthetic increment for this service."""
    query = f'increase(synthetic_probe_runs_total{{service_name="{service}"}}[5m]) > 0'
    end = time.time() + timeout_s
    start = time.time()
    while time.time() < end:
        status, body = http_get(f"{prom_url}/api/v1/query?query={urllib.parse.quote(query)}")
        if status == 200 and isinstance(body, dict) and body.get("data", {}).get("result"):
            return StageResult("prometheus", prom_url, True, "metric found",
                              int((time.time() - start) * 1000))
        time.sleep(2)
    return StageResult("prometheus", prom_url, False, "metric not found in timeout",
                      int((time.time() - start) * 1000))

def wait_for_loki(trace_id: str, loki_url: str, timeout_s: int = 30) -> StageResult:
    """Stage 3: query Loki for the synthetic log line carrying the trace_id."""
    query = f'{{service_name=~"myapp.*"}} |~ "(?i)synthetic" |= "{trace_id}"'
    end = time.time() + timeout_s
    start = time.time()
    while time.time() < end:
        url = (f"{loki_url}/loki/api/v1/query_range"
               f"?query={urllib.parse.quote(query)}&limit=1")
        status, body = http_get(url)
        if status == 200 and isinstance(body, dict) and body.get("data", {}).get("result"):
            return StageResult("loki", loki_url, True, "log found",
                              int((time.time() - start) * 1000))
        time.sleep(2)
    return StageResult("loki", loki_url, False, "log not found in timeout",
                      int((time.time() - start) * 1000))

def wait_for_tempo(trace_id: str, tempo_url: str, timeout_s: int = 30) -> StageResult:
    """Stage 4: query Tempo for the synthetic trace by id."""
    end = time.time() + timeout_s
    start = time.time()
    while time.time() < end:
        status, body = http_get(f"{tempo_url}/api/traces/{trace_id}")
        # Tempo returns 200 + JSON for found traces, 404 for unknown
        if status == 200 and isinstance(body, dict) and body.get("batches"):
            return StageResult("tempo", tempo_url, True, "trace found",
                              int((time.time() - start) * 1000))
        time.sleep(2)
    return StageResult("tempo", tempo_url, False, "trace not found in timeout",
                      int((time.time() - start) * 1000))

def check_grafana_datasources(grafana_url: str) -> StageResult:
    """Stage 5: Grafana is reachable AND each datasource health endpoint returns OK."""
    start = time.time()
    status, body = http_get(f"{grafana_url}/api/health")
    if status != 200:
        return StageResult("grafana", grafana_url, False, f"health HTTP {status}", 0)

    # /api/datasources requires basic auth (admin:admin in dev) — pass via URL
    auth = os.environ.get("GRAFANA_BASIC_AUTH", "admin:admin")
    url = f"http://{auth}@{grafana_url.split('://', 1)[1]}/api/datasources"
    status, body = http_get(url)
    if status != 200 or not isinstance(body, list):
        return StageResult("grafana", grafana_url, False, "cannot list datasources",
                          int((time.time() - start) * 1000))

    failing = []
    for ds in body:
        uid = ds.get("uid")
        name = ds.get("name")
        s, b = http_get(f"http://{auth}@{grafana_url.split('://', 1)[1]}/api/datasources/uid/{uid}/health")
        if s != 200 or (isinstance(b, dict) and b.get("status") != "OK"):
            failing.append(f"{name}({uid})")

    elapsed = int((time.time() - start) * 1000)
    if failing:
        return StageResult("grafana", grafana_url, False,
                          f"failing: {', '.join(failing)}", elapsed)
    return StageResult("grafana", grafana_url, True,
                      f"{len(body)} datasources healthy", elapsed)

def check_alertmanager_smoke(am_url: str, max_age_s: int = 7200) -> StageResult:
    """Stage 6: a smoke alert has reached the smoke-receiver recently."""
    start = time.time()
    receiver_url = os.environ.get("SMOKE_RECEIVER_URL",
                                  "http://smoke-receiver:5001/last")
    status, body = http_get(receiver_url)
    if status != 200:
        return StageResult("alertmanager", am_url, False,
                          f"smoke receiver unreachable: HTTP {status}",
                          int((time.time() - start) * 1000))
    age_s = int(time.time() - body.get("received_at_epoch", 0))
    if age_s > max_age_s:
        return StageResult("alertmanager", am_url, False,
                          f"last smoke receipt {age_s}s ago (max {max_age_s}s)",
                          int((time.time() - start) * 1000))
    return StageResult("alertmanager", am_url, True,
                      f"last smoke receipt {age_s}s ago",
                      int((time.time() - start) * 1000))


# ─── Result metric push ──────────────────────────────────────────────────
def emit_result_metric(passed: bool) -> None:
    """Push a result gauge via Pushgateway. absent_over_time → SyntheticProbeRunnerDown."""
    pgw = os.environ.get("PUSHGATEWAY_URL")
    if not pgw:
        return  # dev mode without pushgateway — skip silently
    value = "1" if passed else "0"
    url = (f"{pgw}/metrics/job/telemetry-health-check"
           f"/instance/{os.environ.get('HOSTNAME', 'local')}")
    body = (f'synthetic_health_check_result{{passed="{value}"}} {value}\n'
            f'synthetic_health_check_timestamp_seconds {int(time.time())}\n').encode()
    try:
        http_post(url, body, {"Content-Type": "text/plain"}, timeout=5)
    except Exception:
        pass  # don't fail the run on metric push failure


# ─── Runner ──────────────────────────────────────────────────────────────
def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--hosts", nargs="+", required=True,
                   help="Service URLs, e.g. http://myapp-apihost:5101")
    p.add_argument("--probe-token", default=os.environ.get("PROBE_TOKEN", ""))
    p.add_argument("--prometheus", default=os.environ.get("PROMETHEUS_URL", "http://prometheus:9090"))
    p.add_argument("--loki", default=os.environ.get("LOKI_URL", "http://loki:3100"))
    p.add_argument("--tempo", default=os.environ.get("TEMPO_URL", "http://tempo:3200"))
    p.add_argument("--grafana", default=os.environ.get("GRAFANA_URL", "http://grafana:3000"))
    p.add_argument("--alertmanager", default=os.environ.get("ALERTMANAGER_URL", "http://alertmanager:9093"))
    p.add_argument("--timeout", type=int, default=30, help="Per-backend poll timeout (s)")
    p.add_argument("--json", action="store_true", help="Emit JSON report")
    args = p.parse_args()

    all_passed = True
    reports: list[ProbeReport] = []

    for host in args.hosts:
        report = call_probe(host, args.probe_token)
        if not report.stages[0].passed:
            all_passed = False
            reports.append(report)
            continue
        report.stages.append(wait_for_prometheus(report.service, args.prometheus, args.timeout))
        report.stages.append(wait_for_loki(report.trace_id, args.loki, args.timeout))
        report.stages.append(wait_for_tempo(report.trace_id, args.tempo, args.timeout))
        if not report.all_passed:
            all_passed = False
        reports.append(report)

    cluster_stages: list[StageResult] = [
        check_grafana_datasources(args.grafana),
        check_alertmanager_smoke(args.alertmanager),
    ]
    if not all(s.passed for s in cluster_stages):
        all_passed = False

    emit_result_metric(all_passed)

    if args.json:
        print(json.dumps({
            "reports": [asdict(r) for r in reports],
            "cluster_stages": [asdict(s) for s in cluster_stages],
            "all_passed": all_passed,
            "timestamp": int(time.time()),
        }, indent=2))
    else:
        for r in reports:
            print(f"\n=== {r.host} (trace={r.trace_id[:8]}…, probe={r.probe_id[:8]}…) ===")
            for s in r.stages:
                mark = "\u2713" if s.passed else "\u2717"
                print(f"  {mark} {s.stage:14s} {s.detail}  ({s.elapsed_ms} ms)")
        print("\n=== cluster-wide ===")
        for s in cluster_stages:
            mark = "\u2713" if s.passed else "\u2717"
            print(f"  {mark} {s.stage:14s} {s.detail}  ({s.elapsed_ms} ms)")
        print(f"\nResult: {'PASS' if all_passed else 'FAIL'}")

    return 0 if all_passed else 1


if __name__ == "__main__":
    sys.exit(main())
```

Save as `scripts/telemetry-health-check.py` and run locally:

```bash
python3 scripts/telemetry-health-check.py \
  --hosts http://localhost:5101 \
  --probe-token $PROBE_TOKEN \
  --json | jq .
```

### 21.6 Scheduling — K8s CronJob + docker-compose service

**K8s CronJob** (prod):

```yaml
# observability/k8s/telemetry-probe-cronjob.yaml
apiVersion: batch/v1
kind: CronJob
metadata:
  name: telemetry-health-check
  namespace: observability
spec:
  schedule: "*/5 * * * *"            # every 5 minutes — tune per environment
  concurrencyPolicy: Forbid          # never overlap runs
  successfulJobsHistoryLimit: 3
  failedJobsHistoryLimit: 10         # keep failures for forensics
  jobTemplate:
    spec:
      backoffLimit: 0                # do NOT retry — failure is the signal
      template:
        spec:
          restartPolicy: Never
          containers:
            - name: probe
              image: python:3.12-slim
              command: [python3, /scripts/telemetry-health-check.py]
              args:
                - --hosts
                - http://myapp-apihost.myapp.svc.cluster.local:5101
                - http://myapp-ordersapi.myapp.svc.cluster.local:5102
                - http://myapp-invoicesapi.myapp.svc.cluster.local:5103
                - http://myapp-shippingapi.myapp.svc.cluster.local:5104
                - --json
              env:
                - name: PROBE_TOKEN
                  valueFrom: { secretKeyRef: { name: probe-token, key: token } }
                - name: SMOKE_RECEIVER_URL
                  value: http://smoke-receiver.observability.svc.cluster.local:5001/last
                - name: PUSHGATEWAY_URL
                  value: http://pushgateway.observability.svc.cluster.local:9091
              volumeMounts:
                - { name: scripts, mountPath: /scripts, readOnly: true }
          volumes:
            - name: scripts
              configMap: { name: telemetry-probe-scripts }
```

**docker-compose service** (dev) — add to `observability/docker-compose.observability.yml`:

```yaml
  telemetry-probe:
    image: python:3.12-slim
    container_name: telemetry-probe
    entrypoint: ["/bin/sh", "-c"]
    command:
      - |
        python3 /scripts/telemetry-health-check.py --hosts http://myapp-apihost:5101 --json > /tmp/report.json
        cat /tmp/report.json
        exit $$(jq '.all_passed | if . then 0 else 1 end' /tmp/report.json)
    environment:
      PROBE_TOKEN: ${PROBE_TOKEN:-dev-only}
      SMOKE_RECEIVER_URL: http://smoke-receiver:5001/last
    volumes:
      - ../scripts/telemetry-health-check.py:/scripts/telemetry-health-check.py:ro
    networks: [obs, app]
    depends_on: [otel-agent, prometheus, loki, tempo, grafana, smoke-receiver]
    restart: "no"
```

For continuous dev probing, wrap in a `while true; do …; sleep 300; done` loop in a separate `telemetry-probe-loop` service.

### 21.7 Smoke-test receiver — `scripts/alertmanager-smoke-receiver.py`

A tiny HTTP server (stdlib only) that records the last smoke alert it received and exposes both a `/last` endpoint (polled by the runner in §21.5) and a `/metrics` endpoint (scraped by Prometheus).

```python
#!/usr/bin/env python3
"""
Tiny Alertmanager webhook receiver for smoke-test alerts.

- POST /         — Alertmanager webhook target; records the last smoke alert
- GET  /last     — JSON of the last smoke alert received (polled by runner)
- GET  /metrics  — Prometheus exposition format (scraped by Prometheus)

The runner uses /last to confirm Alertmanager delivered a smoke alert.
Prometheus scrapes /metrics for `smoke_receiver_last_receipt_epoch`, which
the SyntheticAlertmanagerSmokeMissing alert watches.
"""
import json, os, time
from http.server import HTTPServer, BaseHTTPRequestHandler

STATE_FILE = os.environ.get("STATE_FILE", "/var/lib/probe/last_receipt.json")

class SmokeHandler(BaseHTTPRequestHandler):
    def _send(self, code, body):
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.end_headers()
        self.wfile.write(json.dumps(body).encode())

    def do_POST(self):
        length = int(self.headers.get("Content-Length", 0))
        body = self.rfile.read(length)
        try:
            payload = json.loads(body)
        except json.JSONDecodeError:
            self._send(400, {"error": "invalid json"}); return

        for alert in payload.get("alerts", []):
            if alert.get("labels", {}).get("smoke_test") == "true":
                os.makedirs(os.path.dirname(STATE_FILE), exist_ok=True)
                with open(STATE_FILE, "w") as f:
                    json.dump({
                        "received_at": alert.get("startsAt"),
                        "received_at_epoch": time.time(),
                        "alert": alert,
                    }, f)
                break
        self._send(200, {"ok": True})

    def do_GET(self):
        if self.path == "/metrics":
            epoch = 0
            if os.path.exists(STATE_FILE):
                with open(STATE_FILE) as f:
                    epoch = int(json.load(f).get("received_at_epoch", 0))
            self.send_response(200)
            self.send_header("Content-Type", "text/plain; version=0.0.4")
            self.end_headers()
            payload = (
                "# HELP smoke_receiver_last_receipt_epoch Unix timestamp of last smoke alert received\n"
                "# TYPE smoke_receiver_last_receipt_epoch gauge\n"
                f"smoke_receiver_last_receipt_epoch {epoch}\n"
            )
            self.wfile.write(payload.encode())
            return
        if self.path == "/last":
            if not os.path.exists(STATE_FILE):
                self._send(404, {"error": "no smoke alert received yet"}); return
            with open(STATE_FILE) as f:
                self._send(200, json.load(f)); return
        self._send(404, {"error": "not found"})

if __name__ == "__main__":
    port = int(os.environ.get("PORT", "5001"))
    print(f"smoke-receiver listening on :{port}, state file: {STATE_FILE}")
    HTTPServer(("0.0.0.0", port), SmokeHandler).serve_forever()
```

Add to `docker-compose.observability.yml`:

```yaml
  smoke-receiver:
    image: python:3.12-slim
    container_name: smoke-receiver
    command: python3 /scripts/alertmanager-smoke-receiver.py
    environment:
      STATE_FILE: /var/lib/probe/last_receipt.json
    volumes:
      - ../scripts/alertmanager-smoke-receiver.py:/scripts/alertmanager-smoke-receiver.py:ro
      - smoke-data:/var/lib/probe
    ports: ["5001:5001"]
    networks: [obs]
    restart: unless-stopped
```

Add a Prometheus scrape job (in `prometheus/prometheus.yml`):

```yaml
scrape_configs:
  - job_name: smoke-receiver
    static_configs:
      - targets: ['smoke-receiver:5001']
```

### 21.8 Synthetic alert rules — `prometheus/rules/synthetic-alerting.yml`

```yaml
# observability/prometheus/rules/synthetic-alerting.yml
groups:
  - name: synthetic-alerting
    interval: 30s
    rules:

      # ── Recording: probe last-seen per service ──
      - record: synthetic:probe:last_seen_timestamp
        expr: |
          max by (service.name) (synthetic_probe_runs_total{synthetic="true"})

      # ── Recording: hourly smoke counter (increments each hour) ──
      # Used by the SyntheticAlertmanagerSmokeTest alert below.
      - record: synthetic:smoke:hour
        expr: floor(time() / 3600)

      # ── Alert: probe hasn't fired in 10m (per service) ──
      - alert: SyntheticProbeMissing
        expr: |
          (time() - synthetic:probe:last_seen_timestamp) > 600
          or
          absent_over_time(synthetic_probe_runs_total{synthetic="true"}[10m])
        for: 2m
        labels:
          severity: page
          synthetic: "true"
        annotations:
          summary: "Telemetry probe stopped for {{ $labels.service.name }}"
          description: "No synthetic probe from {{ $labels.service.name }} in 10m. The observability pipeline for this service may be broken."

      # ── Alert: runner itself is dead (no result metric in 15m) ──
      - alert: SyntheticProbeRunnerDown
        expr: absent_over_time(synthetic_health_check_result[15m])
        for: 5m
        labels:
          severity: page
          synthetic: "true"
        annotations:
          summary: "Telemetry health-check runner is down"
          description: "No synthetic_health_check_result metric in 15m — the CronJob that validates the pipeline is not running or cannot reach Pushgateway."

      # ── Smoke alert: fires hourly to validate the full Alertmanager path ──
      # Routes to the smoke-receiver, not on-call, via the synthetic route in §21.9.
      - alert: SyntheticAlertmanagerSmokeTest
        expr: increase(synthetic:smoke:hour[5m]) > 0
        for: 0m
        labels:
          severity: none
          synthetic: "true"
          smoke_test: "true"
        annotations:
          summary: "Hourly Alertmanager smoke test"
          description: "Synthetic alert fired. If this doesn't arrive at the smoke receiver within 2m, the alerting pipeline is broken."

      # ── Alert: smoke alert didn't reach receiver in 2h ──
      - alert: SyntheticAlertmanagerSmokeMissing
        expr: |
          (time() - smoke_receiver_last_receipt_epoch) > 7200
          or
          absent(smoke_receiver_last_receipt_epoch)
        for: 10m
        labels:
          severity: page
          synthetic: "true"
        annotations:
          summary: "Alertmanager smoke alert not delivered"
          description: "No smoke alert has reached the receiver in 2h. Check Alertmanager routing, webhook auth, and the smoke-receiver pod."
```

### 21.9 Routing synthetic alerts in Alertmanager

Add a synthetic block **before** the catch-all severity routes in `alertmanager/alertmanager.yml` (§14) so smoke-test alerts go to the smoke receiver instead of paging on-call:

```yaml
# observability/alertmanager/alertmanager.yml — extend the routes block
route:
  # ... existing top-level fields
  routes:
    # 1. Synthetic smoke test → smoke receiver only, immediate, no repeat throttle
    - matchers:
        - synthetic = "true"
        - smoke_test = "true"
      receiver: smoke-receiver
      group_wait: 0s
      repeat_interval: 1h

    # 2. Other synthetic alerts (probe missing, runner down, smoke missing)
    #    → these ARE real incidents (telemetry is broken) → page on-call
    - matchers:
        - synthetic = "true"
      receiver: oncall-pager
      group_wait: 10s
      repeat_interval: 1h

    # 3. Existing severity-based routes (unchanged)
    - matchers:
        - severity = page
      receiver: oncall-pager
      group_wait: 10s
      repeat_interval: 1h
    # ...

receivers:
  # ... existing receivers
  - name: smoke-receiver
    webhook_configs:
      - url: http://smoke-receiver.observability.svc.cluster.local:5001/
        send_resolved: true
```

### 21.10 Filtering synthetic traffic out of SLO metrics

Synthetic probes are **not real user traffic**. They must NOT contribute to your availability/latency SLOs or your dashboards. Two changes:

**1. Update recording rules (§13.1)** — add `synthetic!="true"` filter to every rule that aggregates over `http_server_*` or `wolverine_*`:

```yaml
# Before:
- record: service:http_requests:rate5m
  expr: |
    sum by (service_name, http_route, http_status_code, deployment_environment)
    (rate(http_server_request_duration_seconds_count[5m]))

# After:
- record: service:http_requests:rate5m
  expr: |
    sum by (service_name, http_route, http_status_code, deployment_environment)
    (rate(http_server_request_duration_seconds_count{synthetic!="true"}[5m]))
```

Apply the same `{synthetic!="true"}` filter to every SLO-feeding recording rule and every alert expression in `service-alerting.yml` (§13.2). Without this, your error-rate SLO will count probe failures as real failures.

**2. Tag probe signals explicitly** — set `synthetic="true"` on the probe activity, the metric, and a logger scope. Do not assume the OTel logs SDK copies arbitrary activity attributes onto log records; it copies the trace/span ids and the scopes you create. If you add a new synthetic signal, set the attribute yourself.

### 21.11 Probe endpoint auth — `ProbePolicy`

The probe endpoint is production-available, so it must be authenticated. Use a dedicated policy that accepts either a probe token (for the runner) or an admin JWT (for manual probing from the team). Register the policy in **every** host that exposes the endpoint, not only ApiHost. Set `Probe:ServiceName` in each host so the endpoint reports the same name as its resource (`myapp.apihost`, `myapp.ordersapi`, …).

```csharp
// src/Services/MyApp.ApiHost/Infrastructure/ProbeAuthExtensions.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace MyApp.Observability;

public static class ProbeAuthExtensions
{
    public const string ProbePolicy = "ProbePolicy";

    public static IServiceCollection AddProbeAuthorization(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(ProbePolicy, policy =>
                policy.RequireAssertion(ctx =>
                {
                    // Probe token (CronJob → host)
                    var expected = configuration["Probe:Token"];
                    if (!string.IsNullOrEmpty(expected))
                    {
                        var auth = ctx.HttpContext.Request.Headers.Authorization.ToString();
                        if (auth == $"Bearer {expected}")
                            return true;
                    }
                    // Admin JWT (team → host, manual probing)
                    return ctx.User.IsInRole(MyApp.Auth.Jwt.Roles.Admin);
                }));
        });
        return services;
    }
}
```

Wire it in `Program.cs`:

```csharp
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddMyAppObservability(builder.Configuration,
        serviceName: "myapp.apihost",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0")
    .AddProbeAuthorization(builder.Configuration)   // ← NEW
    .AddModularEfCore()
    // ...
```

Generate the probe token once per environment and store as a K8s Secret:

```bash
PROBE_TOKEN=$(openssl rand -hex 32)
kubectl create secret generic probe-token \
  --from-literal=token=$PROBE_TOKEN -n observability
```

For dev, add to `.env`:

```env
Probe__Token=dev-only-change-me
```

### 21.12 CI integration — post-deploy smoke test

Run a one-shot probe as a post-deploy gate in your deploy pipeline. A failed exit code blocks the rollout.

```yaml
# .github/workflows/deploy.yml — post-deploy stage
- name: Verify telemetry pipeline
  env:
    PROBE_TOKEN: ${{ secrets.PROBE_TOKEN }}
  run: |
    python3 scripts/telemetry-health-check.py \
      --hosts https://api.myapp.com \
      --prometheus https://prom.myapp.com \
      --loki https://loki.myapp.com \
      --tempo https://tempo.myapp.com \
      --grafana https://grafana.myapp.com \
      --alertmanager https://am.myapp.com \
      --timeout 60
```

For progressive rollouts, run the probe against the canary endpoint first; only proceed to full rollout if the probe passes.

### 21.13 What to alert on (summary)

| Alert | Source | Severity | Trigger |
|---|---|---|---|
| `SyntheticProbeMissing` | Prometheus rule | page | No probe metric from a service in 10m |
| `SyntheticProbeRunnerDown` | Prometheus rule | page | No `synthetic_health_check_result` in 15m (runner is broken) |
| `SyntheticAlertmanagerSmokeTest` | Prometheus rule (hourly) | none | Smoke alert fired to validate AM path |
| `SyntheticAlertmanagerSmokeMissing` | Prometheus rule | page | No smoke receipt at receiver in 2h |

All four carry `synthetic="true"`. The smoke test has `smoke_test="true"` additionally, routing it to the smoke receiver instead of on-call.

### 21.14 Dashboard panel — "Telemetry Pipeline Health"

Add to your `service-overview.json` dashboard (§15.4):

| Panel | Query |
|---|---|
| Probe success per service (last 1h) | `sum by (service_name) (increase(synthetic_probe_runs_total{synthetic="true"}[1h]))` |
| Runner status | `up{job="telemetry-probe"}` or `synthetic_health_check_result` |
| Last smoke receipt age | `time() - smoke_receiver_last_receipt_epoch` |
| Probe end-to-end duration | `histogram_quantile(0.95, sum by (instance, le) (rate(synthetic_health_check_duration_seconds_bucket[5m])))` (if you add a duration histogram to the runner) |

### 21.15 Best-practice checklist for the suite

- [ ] Probe endpoint exists in **every** host (ApiHost + each extracted service + gateway)
- [ ] Probe endpoint is auth-gated by `ProbePolicy`, never anonymous in prod
- [ ] `MyApp.Synthetic` meter + activity source registered in `AddMyAppObservability`
- [ ] Every synthetic signal carries `synthetic="true"` attribute
- [ ] Recording rules in §13.1 filter `{synthetic!="true"}` for SLO inputs
- [ ] Alert expressions in §13.2 filter `{synthetic!="true"}` for SLO alerts
- [ ] Runner pushes `synthetic_health_check_result` to Pushgateway
- [ ] `SyntheticProbeMissing` alert defined with per-service label
- [ ] `SyntheticProbeRunnerDown` alert defined (catches runner death)
- [ ] Smoke receiver running, scraped by Prometheus
- [ ] `SyntheticAlertmanagerSmokeTest` fires hourly
- [ ] `SyntheticAlertmanagerSmokeMissing` alert defined
- [ ] Alertmanager routes synthetic alerts before severity routes
- [ ] CI runs `telemetry-health-check.py` as a post-deploy gate
- [ ] One Grafana dashboard panel shows probe status
- [ ] Probe token stored as a K8s Secret, rotated quarterly

---

## 22. Common pitfalls

| Symptom | Cause | Fix |
|---|---|---|
| No traces in Tempo, but logs arrive | Tail sampling dropping everything | Temporarily set `sampling_percentage: 100` in `gateway.yaml` |
| All logs land in one Loki stream | `service.name` not on resource | Check `ConfigureResource(r => r.AddService(...))` is called |
| Prometheus has 100k+ series for `http.server.request.duration` | `http.target` (raw URL) leaking as a label | Enable `filter/cardinality` on gateway (§6.2) |
| No Wolverine spans | `"Wolverine"` source not registered | Add `.AddSource("Wolverine")` (§10) |
| No Wolverine metrics | Meter registered without a wildcard | Use `.AddMeter("Wolverine*")` (§10) |
| EF Core span shows `db.statement = "SELECT ..."` | EF instrumentation emits old attributes | The 1.13-beta package does not expose `SetDbStatementForText`; filter the attribute in the collector or pin a package that does |
| Dashboards empty after first deploy | Service variable uses `service_name` (underscore) but data has `service.name` (dot) | OTel → Prometheus converts `.` to `_` in labels; both queries use `service_name` consistently |
| Collector logs `sending queue full` | `queue_size` too small for traffic | Bump `queue_size` in `agent.yaml` and HPA the gateway |
| Grafana "Trace not found" when clicking a log link | Tempo retention < Loki retention | Match retention windows or use Loki search-to-trace fallback |
| OTLP app → collector refused | Endpoint is `http://` but collector only listens on gRPC `:4317` | Use `OtlpExportProtocol.Grpc` (set in §7.1) — already done |

---

## 23. Migration path — what to do week by week

### Week 1 — Local stack + ApiHost only
- Bring up `docker-compose.observability.yml`.
- Wire `AddMyAppObservability` into `MyApp.ApiHost` only.
- Verify metrics + logs + traces show in Grafana.
- Iterate on the `service-overview.json` dashboard.

### Week 2 — Extracted services
- Reference `MyApp.Observability` from `OrdersApi`, `InvoicesApi`, and `ShippingApi`, and add the one-line `AddMyAppObservability` call.
- Confirm `.AddSource("Wolverine")` and `.AddMeter("Wolverine*")` in every host.
- Verify trace links across the RabbitMQ hop (ApiHost publish → OrdersApi consume).

### Week 3 — Gateway + alerts
- Add OTel to `MyApp.Gateway` (YARP).
- Define the four SLOs in §16.
- Wire Alertmanager → PagerDuty / Opsgenie / Slack.

### Week 4 — Production
- Move to K8s DaemonSet + Deployment topology (§18.1).
- Move Loki + Tempo backends to S3 (§18.2).
- Set retention windows (§18.3).
- TLS everywhere (§18.4).

### Week 5 — Synthetic suite + cost tuning
- Add the `TelemetryProbeEndpoint` (§21.4) to every host.
- Deploy the runner CronJob (§21.6) and smoke-receiver (§21.7).
- Wire the synthetic alert rules (§21.8) + Alertmanager synthetic routes (§21.9).
- Add the `synthetic!="true"` filter to all SLO recording rules (§21.10).
- Add the post-deploy CI gate (§21.12).
- Reduce tail-sampling percentage from 100% → 10% once you trust the patterns.
- Drop `hostmetrics` from agent if you have node-exporter running.
- Move long-term metrics retention to Mimir if Prometheus churn > 200k series.

---

## 24. Appendix A — package version matrix

Verified against the local NuGet cache for `net10.0`:

| Package | Version | Notes |
| --- | --- | --- |
| `OpenTelemetry` | `1.17.0` | Stable core |
| `OpenTelemetry.Extensions.Hosting` | `1.17.0` | Provides `AddOpenTelemetry()` |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | `1.17.0` | OTLP gRPC + HTTP |
| `OpenTelemetry.Instrumentation.AspNetCore` | `1.17.0` | `http.server.*` |
| `OpenTelemetry.Instrumentation.Http` | `1.17.0` | Outbound HttpClient |
| `OpenTelemetry.Instrumentation.EntityFrameworkCore` | `1.13.0-beta.1` | Trace spans only; no metrics |
| `OpenTelemetry.Instrumentation.Runtime` | `1.15.1` | GC / ThreadPool |
| `WolverineFx` | `6.51.2` | OpenTelemetry is built in |
| `otel/opentelemetry-collector-contrib` | `0.110.0` | Collector |
| `prom/prometheus` | `v2.55.1` | Remote-write receiver |
| `grafana/loki` | `3.2.1` | OTLP HTTP ingestion |
| `grafana/tempo` | `2.6.0` | OTLP + TraceQL |
| `grafana/grafana` | `11.3.0` | Correlated datasources |
| `prom/alertmanager` | `v0.27.0` | Routing |

Do not add `WolverineFx.OpenTelemetry`; it does not exist.

## 25. Appendix B — glossary

- **OTLP** — OpenTelemetry Protocol, gRPC or HTTP. The wire format everything in this doc speaks.
- **Resource** — attributes describing *who* emitted the telemetry (`service.name`, `host.name`, `deployment.environment`). Flows into every metric label set, every log's structured metadata, every span's resource.
- **Span** — a unit of work in a trace. Has a parent, optional links, attributes, events, status.
- **Span link** — a non-parent relationship between two spans (used by Wolverine across the RabbitMQ hop).
- **Exemplar** — a metric sample that carries a `trace_id`. Lets you click from a Grafana graph to a Tempo trace.
- **Tail sampling** — sampling decisions made *after* a trace completes (vs head sampling at the source). Lets you keep 100% of errors + 10% of normal traffic.
- **Recording rule** — a pre-computed PromQL expression stored as a new time series. Cheaper than recomputing on every dashboard load.
- **Burn rate** — how fast you're consuming your error budget. `14.4x` means you'll exhaust a 30-day budget in 1 hour.

## 26. Appendix C — references

- OpenTelemetry .NET: https://opentelemetry.io/docs/instrumentation/net/
- OTel Collector configs: https://github.com/open-telemetry/opentelemetry-collector-contrib/tree/main/examples
- Prometheus OTLP receiver: https://prometheus.io/docs/prometheus/latest/feature_flags/#otlp-receiver
- Loki OTLP: https://grafana.com/docs/loki/latest/send-data/otel/
- Tempo OTLP + TraceQL: https://grafana.com/docs/tempo/latest/configuration/#otel
- Grafana trace/log correlation: https://grafana.com/docs/grafana/latest/datasources/tempo/configure-tempo-data-source/#traces-to-logs
- Wolverine OTel: https://wolverinefx.net/guide/observability
- Multi-window burn rate alerting: https://sre.google/workbook/alerting-on-slos/

---

## 27. Checklist — before you ship

- [ ] `Directory.Packages.props` has all OTel packages at the pinned versions
- [ ] Each host csproj references the OTel packages
- [ ] One host-only `Shared/MyApp.Observability` project is referenced by every host
- [ ] Each host's `Program.cs` calls `AddMyAppObservability(...)` once (an `Add*` call, not middleware)
- [ ] `appsettings.json` + `.env` set `OTEL_EXPORTER_OTLP_ENDPOINT` and `OTEL_DEPLOYMENT_ENVIRONMENT`
- [ ] Every host registers `.AddSource("Wolverine")` and `.AddMeter("Wolverine*")`
- [ ] `docker compose -f docker-compose.dev.yml up -d --build` runs the full dev stack locally
- [ ] `agent.yaml` + `gateway.yaml` configs reviewed against your traffic (queue sizes, sampling)
- [ ] Loki `distributor.otlp_config.default_resource_attributes_as_index_labels` is a short, low-cardinality list
- [ ] Prometheus `--web.enable-remote-write-receiver` + `exemplar-storage` set
- [ ] Tempo `metrics_generator` writes back to Prometheus
- [ ] Grafana datasources provisioned with trace↔log↔metric cross-links
- [ ] Recording + alerting rules committed in `prometheus/rules/`
- [ ] Alertmanager routes wired to real receivers (PagerDuty / Slack)
- [ ] SLOs documented next to each service
- [ ] One dashboard JSON per service type, tagged + templated
- [ ] Retention windows match compliance requirements
- [ ] TLS between collector and backends in prod
- [ ] No PII in trace attributes, metric labels, or log bodies
- [ ] `otelcol validate`, `promtool check config`, and `promtool check rules` pass in CI
- [ ] `dotnet test tests/YourApp.Tests.Architecture` passes (telemetry must not break layering or endpoint rules)
- [ ] **Synthetic suite** — `TelemetryProbeEndpoint` deployed in every host (§21.4)
- [ ] **Synthetic suite** — the probe counter has no per-probe label (no unbounded cardinality)
- [ ] **Synthetic suite** — `MyApp.Synthetic` meter + activity source registered (§21.4)
- [ ] **Synthetic suite** — probe endpoint auth-gated by `ProbePolicy` (§21.11)
- [ ] **Synthetic suite** — runner CronJob scheduled every 5m in prod (§21.6)
- [ ] **Synthetic suite** — smoke-receiver deployed + scraped by Prometheus (§21.7)
- [ ] **Synthetic suite** — synthetic alert rules committed (§21.8)
- [ ] **Synthetic suite** — Alertmanager synthetic routes added before severity routes (§21.9)
- [ ] **Synthetic suite** — `synthetic!="true"` filter on every SLO recording rule (§21.10)
- [ ] **Synthetic suite** — CI post-deploy gate runs `telemetry-health-check.py` (§21.12)
- [ ] **Synthetic suite** — "Telemetry Pipeline Health" dashboard panel added (§21.14)
---

## 28. Open gaps — required before calling this production-ready

These are not implementation details; they change the design. Each one needs a decision and a test.

| Area | Gap | Acceptance test |
| --- | --- | --- |
| Security | App → collector and collector → backends use plaintext (`tls.insecure: true`) | mTLS or TLS with rotated certs; collector rejects plaintext |
| Access control | Grafana is `admin/admin`; Loki, Tempo, Prometheus ports are unauthenticated | SSO/proxy in front of Grafana; backends bound to a private network |
| Privacy | PII redaction covers span attributes only; logs can contain user identifiers | Log redaction or a logging policy; a test that a known email never reaches Loki |
| Cardinality | No label budget or relabel rules; probe no longer leaks, but HTTP labels are untested at scale | A series-count budget per service, enforced in CI or in Prometheus |
| HA | Single Prometheus/Loki/Tempo/Alertmanager | Replication or a managed backend; a node-loss test |
| Backup/DR | No backup or restore procedure | Restore a day of traces/logs/metrics into a clean environment |
| Retention/cost | Retention is a number, not a budget | Ingest estimate (GB/day) and a monthly cost model |
| SLO policy | Alerts exist; no error-budget policy, escalation, or runbook | An exercised page → runbook → postmortem |
| Sampling | Head and tail sampling are presented as composable; they are not for error coverage | A test that an error is never dropped by the SDK before the collector |
| Multi-tenancy | No tenant isolation or per-tenant limits | Two tenants cannot read each other's data |
| Config validation | Configs are pasted, not validated | `otelcol validate`, `promtool check config`, `promtool check rules` in CI |
| Upgrades | No pinned compatibility matrix or rollback | A staged collector/backend upgrade with rollback |

Until these exist, keep the “production target” label at the top of this file.
