# Gateway JWT + Scalar

YARP edge + federated OpenAPI. Binding architecture (slices, hosts, Shared kit):  
[`docs/ARCHITECTURE.md`](../ARCHITECTURE.md). Async/DDD walkthrough:  
[`07-async-communication`](../../../../../dev-sr.com/content/learn/dotnet-api/07-async-communication/index.mdx) (must not contradict ARCHITECTURE.md).

## Layout

| Project | URL | Role |
| --- | --- | --- |
| `MyApp.Gateway` | `http://localhost:5100` | YARP + **Scalar** (`/scalar`) |
| `MyApp.ApiHost` | `http://localhost:5101` | Auth + Catalog + **Search** + OpenAPI JSON |
| `MyApp.InvoicesApi` | `http://localhost:5102` | Invoices + `OrderPlaced` consumer |
| `MyApp.OrdersApi` | `http://localhost:5103` | Cart / Orders + product projection |
| `MyApp.ShippingApi` | `http://localhost:5104` | Shipments + `OrderPlaced` consumer |

## Scalar

http://localhost:5100/scalar

| Document | Proxied OpenAPI | Includes |
| --- | --- | --- |
| **Auth + Products** (default) | `/openapi/auth/v1.json` ← ApiHost | `/auth/*`, `/products/*`, **`/search/products`** (tag Search) |
| **Orders + Cart** | `/openapi/orders/v1.json` | `/cart/*`, `/orders/*` |
| **Invoices** | `/openapi/invoices/v1.json` | `/invoices/*` |
| **Shipping** | `/openapi/shipping/v1.json` | `/shipping/*` |

Search is picked up because ApiHost Carter loads every `MyApp.Modules.*` assembly (`ModuleAssemblyCatalog`), OpenAPI documents those routes, and the gateway proxies ApiHost’s `/openapi/v1.json` as the auth document. YARP `search-route` forwards `/search/{**catch-all}` to ApiHost so Try-it works through `:5100`.

## YARP (quick)

| Client | Upstream |
| --- | --- |
| `/auth/*`, `/products/*`, `/search/*` | ApiHost `:5101` |
| `/cart/*`, `/orders/*` | OrdersApi `:5103` |
| `/invoices/*` | InvoicesApi `:5102` |
| `/shipping/*` | ShippingApi `:5104` |

Gateway does not validate JWT; forwards `Authorization`.

## Run

```bash
docker compose up -d   # Rabbit + Elasticsearch
dotnet run --project src/Services/MyApp.ApiHost
dotnet run --project src/Services/MyApp.OrdersApi
dotnet run --project src/Services/MyApp.InvoicesApi
dotnet run --project src/Services/MyApp.ShippingApi
dotnet run --project src/Services/MyApp.Gateway
```

## Tests

`CrossServiceInvoiceTests` · `AsyncMessagingTests`
