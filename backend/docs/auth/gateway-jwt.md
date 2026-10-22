# Gateway JWT (branch `auth-2`)

Minimal multi-service auth demo that continues the monolith work on `auth`.

**Learn lesson (source of truth):**  
[`dev-sr.com/content/learn/dotnet-api/05-auth/03-gateway-jwt.mdx`](../../../../../dev-sr.com/content/learn/dotnet-api/05-auth/03-gateway-jwt.mdx)

## Layout

| Project | URL | Role |
| --- | --- | --- |
| `MyApp.Gateway` | `http://localhost:5100` | YARP + **Scalar** (`/scalar`) federating OpenAPI |
| `MyApp.ApiHost` | `http://localhost:5101` | Token issuer + IAM + **products** (DB) + OpenAPI JSON |
| `MyApp.InvoicesApi` | `http://localhost:5102` | Invoice CRUD + ownership; claim-only `AddMyAppAuth` + Problem Details |
| `MyApp.Auth.Jwt` | — | Shared HS256 + policies + `JwtBearerProblemDetails` + resource ownership |
| `Shared.Common` | — | `Error` / `ToProblem` + `AddSharedExceptionHandling` (Auth + Invoices) |

## Docs (standard)

- **Scalar:** http://localhost:5100/scalar  
  Dropdown: **Auth + Products** (default) · **Invoices**
- Proxied specs: `/openapi/auth/v1.json`, `/openapi/invoices/v1.json`

## YARP (quick)

| Client | Upstream |
| --- | --- |
| `/auth/*`, `/products/*` | AuthApi `:5101` |
| `/invoices/*` | InvoicesApi `:5102` |

Gateway does not validate JWT; forwards `Authorization`.

## Run

```bash
# three terminals from MyApp/backend
dotnet run --project src/Services/MyApp.ApiHost
dotnet run --project src/Services/MyApp.InvoicesApi
dotnet run --project src/Services/MyApp.Gateway
```

`Jwt:Secret` / Issuer / Audience must match on AuthApi (`.env`) and InvoicesApi (`appsettings.json`).

## Design notes

- AuthApi issues JWT + seeds `Permissions.*`; InvoicesApi only validates claims + ownership.
- InvoicesApi has no IAM DB — `Permissions.cs` / `ResourceAuthorization.cs` in `MyApp.Auth.Jwt`.
- Error contract: `application/problem+json` (`INVOICE.*`, `AUTH.*`, validation).
- AuthApi `ConfigureJwtBearer` = ban / `token_version` only (issuer host).

## Tests

`CrossServiceInvoiceTests` — Auth JWT → InvoicesApi create / ownership / Problem Details.
