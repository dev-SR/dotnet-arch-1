# Gateway JWT (branch `auth-2`)

Minimal multi-service auth demo that continues the monolith work on `auth`.

**Learn lesson (source of truth):**  
[`dev-sr.com/content/learn/dotnet-api/05-auth/03-gateway-jwt.mdx`](../../../../../dev-sr.com/content/learn/dotnet-api/05-auth/03-gateway-jwt.mdx)

## Layout

| Project | URL | Role |
| --- | --- | --- |
| `MyApp.Gateway` | `http://localhost:5100` | YARP + **Scalar** (`/scalar`) federating OpenAPI |
| `MyApp.Api` | `http://localhost:5101` | Token issuer + IAM + **products** (DB) + OpenAPI JSON |
| `MyApp.InvoicesApi` | `http://localhost:5102` | Invoice CRUD + ownership; claim-only `AddMyAppAuth` + Problem Details |
| `MyApp.Auth.Jwt` | — | Shared HS256 + policies + `JwtBearerProblemDetails` + resource ownership |
| `Shared.Common` | — | `Error` / `ToProblem` + `AddSharedExceptionHandling` (used by Auth + Invoices) |

## Docs (standard)

Unified API reference on the **gateway** (not private service ports):

- **Scalar:** http://localhost:5100/scalar  
  Document dropdown: **Auth + Products** (default) · **Invoices** (`/invoices` is not under the Auth doc)
- Proxied specs: `/openapi/auth/v1.json`, `/openapi/invoices/v1.json`  
  (aliases under `/scalar/openapi/...`; Try-it rewritten to the gateway base URL)

## Run

```bash
# three terminals from MyApp/backend
dotnet run --project src/Services/MyApp.Api
dotnet run --project src/Services/MyApp.InvoicesApi
dotnet run --project src/Services/MyApp.Gateway
```

`Jwt:Secret` / Issuer / Audience must match on AuthApi (`.env`) and InvoicesApi (`appsettings.json`).

## Design notes

- Same claim shape as Learn 01/02: type `permission`, values `Permissions.*`.
- AuthApi keeps **products**; InvoicesApi is the second host (no product duplication).
- InvoicesApi does **not** hit the IAM database — trusts validated JWT claims + ownership via `sub`.
- Error contract matches AuthApi: `application/problem+json` with `errorCode` / `traceId`  
  (`INVOICE.NOT_FOUND`, `INVOICE.FORBIDDEN`, `AUTH.UNAUTHENTICATED`, validation, …).
- AuthApi still runs `ConfigureJwtBearer` ban / `token_version` checks (issuer host only).
- Gateway does not validate JWT; each API host does.

## Tests

`CrossServiceInvoiceTests` — Auth JWT → InvoicesApi create / ownership / Problem Details.
