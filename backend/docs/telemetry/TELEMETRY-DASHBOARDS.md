# Using the MyApp Grafana Dashboards

This is the **user manual** for the dashboards. It assumes the stack is already running. For setup, see the quickstart.

Open Grafana at **http://localhost:3000** and log in with `admin` / `admin`.

If you are **using** the dashboards, read §1–§8. If you are **generating or editing** them (including with an AI agent), jump to the optional §9.

---

## 1. Find the dashboards

1. Open **http://localhost:3000**.
2. Log in with `admin` / `admin`.
3. In the left sidebar, click **Dashboards** (the four-squares icon).
4. Open the **MyApp** folder.
5. You should see:
   - **MyApp — Service Overview**
   - **MyApp — Traces & Messaging**

The dashboards are provisioned from files, so they appear automatically. If you edit them in the UI, the changes are local to Grafana; the files remain the source of truth.

---

## 2. The controls at the top

Before reading a panel, check these three controls:

**Time range (top right).** This is the most common reason a panel looks empty. Use **Last 15 minutes** or **Last 1 hour** while learning. If you generated traffic earlier, an absolute range that does not include that time will show nothing.

**Refresh (top right).** The dashboards refresh every 15 seconds. You can also click the refresh icon after sending a request.

**Service variable (top left).** Both dashboards have a **Service** dropdown. It is filled from the `service_name` label in Prometheus. Choose one service, or leave **All** selected. The panels update immediately; they do not require a page reload.

If the Service dropdown is empty, Prometheus has not received metrics yet. Generate traffic first, then wait up to 15 seconds.

---

## 3. MyApp — Service Overview

This dashboard answers: **“Is the service healthy, and what is it doing?”**

### The four numbers at the top

**Requests / sec** is the current request rate, excluding `/health` and OpenAPI traffic. A flat zero means no traffic in the selected time range, not necessarily a failure.

**Error rate** is the share of requests that returned a 5xx status. `0%` is good. A sudden rise means the service is failing requests; click through to the **5xx rate by route** panel to see which route.

If there are no 5xx responses, the panel shows `0`, not “No data”. To produce real errors for testing, start the traffic generator with `ERROR_RATE=100` (quickstart §8.2).

**p95 latency** is the response time that 95% of requests are faster than. It is more useful than an average because a few slow requests do not disappear into the mean. If p95 is `250 ms`, 95 out of 100 requests finished in 250 ms or less.

**Active requests** is how many requests are in flight right now. A steadily rising number can mean requests are piling up.

### Traffic and latency

**Request rate by route** shows which endpoints are being called. Each line is one `http_route`. Use it to see whether traffic is coming from login, products, orders, or a background caller.

**p95 latency by route** shows latency per endpoint. A single slow route stands out as a line that is higher than the others.

**5xx rate by route** shows server errors per route. This is the first panel to open when the top-level error rate rises.

### Runtime

**GC heap size** shows how much memory the .NET garbage collector is holding. A sawtooth pattern is normal. A line that only grows may indicate a leak or a workload that allocates faster than it collects.

**GC pause time rate** shows how much time the process spends paused for garbage collection. Occasional small spikes are normal; sustained high values explain latency that is not visible in your handler code.

**Thread pool queue** shows work waiting for a thread. A queue that stays above zero under load means the process does not have enough worker threads, often because handlers are blocking.

### Business metrics

These are the custom counters from the application, not generic HTTP metrics.

**Products created / sec** comes from `myapp_catalog_products_created_total`. It increments when a product is created through the Catalog slice.

**Orders created / sec** comes from `myapp_orders_created_total`. It increments when an order is placed.

**Active orders (placed)** comes from `myapp_orders_active{status="placed"}`. It is an up/down counter: it goes up when an order is placed. A future cancel/ship flow would decrement it.

### Recent logs

The bottom panel shows Loki logs for the selected service. Expand a line to see structured fields, including `trace_id`. Use the time range and service variable to narrow it down.

### Alerts

The bottom of the Service Overview has two alert panels:

**Firing alerts** shows the number of currently firing Prometheus alerts. It is `0` when nothing is firing.

**Active alerts** lists the firing alerts with their labels (`alertname`, `severity`, `service_name`).

These panels read the Prometheus `ALERTS` series. They are **not** Grafana-managed alerts. The rules live in `prometheus-rules.yml` and are evaluated by Prometheus. Grafana only displays their state.

This is intentional: it keeps one source of truth. If you created the same rule again in Grafana Alerting, you would have two alert definitions and two places to maintain.

---

## 4. MyApp — Traces & Messaging

This dashboard answers: **“What happened in a request, and how are messages flowing?”**

### Recent traces

The top panel searches Tempo for traces from the selected service. Each row is one trace. Click a row to open the waterfall. If the panel shows an error, use **Explore → Tempo** with the same service and span filters; the dashboard panel uses the same datasource.

### Recent logs

The second panel shows Loki logs for the selected service.

### Wolverine messages received / sent

These show message throughput by `message_type`. Use them to confirm that a producer and consumer are both active. If sent is non-zero but received is zero, the consumer is not listening or the queue binding is missing.

### Wolverine outbox / inbox

These show messages waiting in the transactional outbox and inbox. A steadily growing outbox means messages are being written but not flushed to RabbitMQ. A growing inbox can mean consumers are not keeping up.

---

## 5. Walkthrough: investigate a slow request

This is the workflow the dashboards are designed for.

1. Open **MyApp — Service Overview**.
2. Set the time range to **Last 15 minutes**.
3. Look at **p95 latency by route**. Find the route with the highest line.
4. Look at **Requests / sec** for the same time window. Was there a traffic spike? If yes, the slow route may simply be overloaded.
5. Look at **GC pause time rate** and **Thread pool queue**. If either is high, the problem may be runtime pressure rather than a specific handler.
6. Open **MyApp — Traces & Messaging**.
7. In **Recent traces**, find a trace around the slow period.
8. Click it. The waterfall shows where the time went:
   - A long `POST /products` bar with a short `CreateProduct` child means the delay is outside the handler.
   - A long `savechanges` span means the database is slow.
   - A long `send ProductCreated` span means RabbitMQ/outbox work is slow.
   - A long `receive ProductCreated` span means the consumer is slow.
9. Click **Logs for this trace** in the trace view to see the log lines from the same request.

---

## 6. Walkthrough: follow one request across services

1. Generate a product with the quickstart’s `POST /products` command.
2. Open **Explore → Tempo**.
3. Set **Query type** to **Search**.
4. Enter **Service Name** `myapp.apihost` and **Span Name** `POST /products`.
5. Click **Run query**, then click the trace at your request time.
6. The waterfall should include:
   - `POST /products` in `myapp.apihost`
   - `CreateProduct` (the custom Catalog span)
   - `savechanges` (EF Core)
   - `send ProductCreated` (Wolverine producer)
   - `receive ProductCreated` in `myapp.ordersapi`
7. Select the `CreateProduct` span. The right panel shows tags such as `myapp.module=catalog`.
8. Click **Logs for this trace** to jump to Loki with the trace id applied.

If you see several `POST /products` rows, they are separate requests. Match the **Start time** to when you ran the command. The first request after startup is often slower because the app, database, and search index are warming up.

---

## 7. Why a panel says “No data”

Check these in order:

1. **Time range.** Use **Last 15 minutes** or **Last 1 hour**. An absolute range that is in the future or before your request returns nothing.
2. **Service variable.** If you picked a service that has no traffic, the panel is empty. Set it to **All**.
3. **Traffic.** Send a request from the quickstart, or start the continuous generator (`./backend/scripts/generate-traffic.sh start`). Wait 15 seconds and refresh.
4. **Prometheus labels.** Open **Explore → Prometheus** and run `up`. If `up` is missing, Prometheus is not scraping the collector.
5. **Collector.** Open `http://localhost:8889/metrics` and confirm the page has `otelcol_*` series.
6. **Loki logs.** “No logs volume available” is only the histogram. The **Logs** panel below it can still contain lines.
7. **Tempo traces.** Tempo needs a few seconds to index a new trace. Re-run the query.

One dashboard-specific detail: when the **Service** variable is set to **All**, the dashboards use `.+`, not `.*`. Loki rejects a query whose matcher can match an empty string, so `{service_name=~".*"}` returns an error and Grafana displays it as “No data”. The provisioned dashboards already use `.+`.

---

## 8. What these dashboards do not cover

These are intentional gaps, not bugs:

- SLOs and error budgets.
- Alert rules and Alertmanager routing beyond the two dev rules. See quickstart §9.3.
- RabbitMQ broker metrics such as queue depth and consumer lag. Use the RabbitMQ UI at `http://localhost:15672`.
- Elasticsearch cluster health. Use `http://localhost:9200`.
- Per-user or per-tenant breakdowns.
- Production retention, sampling, and cost.

The dashboards are for learning the flow and for local debugging. They are not a production SLO dashboard.
---

## 9. Optional: how these dashboards are generated (maintainers / AI agents)

**You do not need this section to use the dashboards.** It is here so a future change can be made safely, especially when an AI agent regenerates the JSON.

### The provisioning chain

Grafana does not read the JSON from the repository directly. The chain is:

1. `observability-dev/grafana-dashboards.yml` tells Grafana to load dashboards from `/etc/grafana/dashboards` into the **MyApp** folder.
2. `docker-compose.dev.yml` mounts:
   - `observability-dev/grafana-dashboards.yml` → `/etc/grafana/provisioning/dashboards/dashboards.yml`
   - `observability-dev/dashboards` → `/etc/grafana/dashboards`
3. Grafana reads every `*.json` in that folder and creates or updates the dashboard by `uid`.

The provider is set to `updateIntervalSeconds: 30`, so file changes are picked up automatically. Restart Grafana when changing the provider file itself.

### Why JSON instead of clicking in the UI

The dashboards contain 14 and 6 panels. Building them by hand in the UI is slow and easy to get wrong. The JSON is also reviewable, diffable, and repeatable. The files are the source of truth.

The one-off generator script used to create the first version was not committed; the JSON is what matters. If you regenerate, treat the JSON as the artifact and commit that.

### What to verify before generating or editing

Do not invent metric or label names. Check them against the running Prometheus first.

List the metric names:

```bash
curl -s http://localhost:9090/api/v1/label/__name__/values \
  | jq -r '.data[]' | grep -E 'myapp|http_server|dotnet|wolverine'
```

Inspect the labels on a specific metric:

```bash
curl -s "http://localhost:9090/api/v1/query?query=http_server_request_duration_seconds_count" \
  | jq '.data.result[0].metric'
```

For this stack the important names are:

- `http_server_request_duration_seconds_count`, `_bucket`, `_sum`
- `http_server_active_requests`
- `http_response_status_code` (not `http_status_code`)
- `http_route` (not `http.route`)
- `myapp_catalog_products_created_total`
- `myapp_catalog_create_duration_milliseconds_bucket`
- `myapp_orders_created_total`
- `myapp_orders_active`
- `dotnet_gc_last_collection_heap_size_bytes`
- `dotnet_gc_pause_time_seconds_total`
- `dotnet_thread_pool_queue_length_total`
- `wolverine_messages_received_Messages_total`
- `wolverine_messages_sent_Messages_total`
- `wolverine_outbox_count_Messages`
- `wolverine_inbox_count_Messages`

### Dashboard JSON anatomy

A dashboard is one JSON object. The fields that matter most:

- `uid` is the stable URL id. Do not change it after people bookmark the dashboard.
- `title` is the display name.
- `time` is the default range (`now-1h`).
- `refresh` is the auto-refresh interval (`15s`).
- `templating.list` defines the **Service** dropdown. Its query is `label_values(http_server_request_duration_seconds_count, service_name)`.
- `panels` is the list of panels. Each panel has `id`, `type`, `title`, `gridPos`, `datasource`, `targets`, and `fieldConfig`.
- `targets[].expr` is the query. For Prometheus it is PromQL; for Loki it is LogQL; for Tempo it is TraceQL.
- `datasource.uid` must match the UIDs in `grafana-datasources.yml` (`prometheus`, `loki`, `tempo`).

Minimal example:

```json
{
  "uid": "example",
  "title": "Example",
  "schemaVersion": 39,
  "time": { "from": "now-1h", "to": "now" },
  "panels": [
    {
      "id": 1,
      "type": "timeseries",
      "title": "Request rate",
      "datasource": { "type": "prometheus", "uid": "prometheus" },
      "gridPos": { "x": 0, "y": 0, "w": 12, "h": 8 },
      "targets": [
        {
          "refId": "A",
          "expr": "sum(rate(http_server_request_duration_seconds_count[5m]))"
        }
      ]
    }
  ]
}
```

### Safe regeneration steps

1. Confirm the metric and label names against Prometheus, as shown above.
2. Generate or edit the JSON.
3. Validate it:

   ```bash
   jq empty observability-dev/dashboards/myapp-service-overview.json
   ```

4. Restart Grafana:

   ```bash
   docker compose -f docker-compose.dev.yml restart grafana
   ```

5. Confirm the dashboard is registered:

   ```bash
   curl -s -u admin:admin "http://localhost:3000/api/search?query=MyApp" \
     | jq -r '.[] | [.title, .uid] | @tsv'
   ```

6. Open the dashboard and check every panel for a red error or “No data”.

### Notes for AI agents

- Query Prometheus for real metric names before writing a target. Do not copy names from another stack.
- Keep `uid` stable. Changing it creates a second dashboard and breaks links.
- Use the datasource UIDs `prometheus`, `loki`, and `tempo`; they are defined in `grafana-datasources.yml`.
- Use `http_response_status_code`, not `http_status_code`, and `http_route`, not `http.route`, in PromQL.
- Use `service_name` in PromQL and `service.name` in TraceQL; they are different systems.
- Use `dotnet_*` runtime metric names, not `process_runtime_dotnet_*`.
- The Tempo traces panel is a dashboard table target. If a Grafana build rejects it, fall back to an Explore link rather than inventing a different query type.
- Validate with `jq empty` and check the Grafana search API after restart.

### Editing from the Grafana UI

You can edit a provisioned dashboard in the UI because `allowUiUpdates` is `true`. If you want that change to survive a rebuild or another environment, export the dashboard JSON and copy it back into `observability-dev/dashboards`. The file, not the Grafana database, is the durable source.
