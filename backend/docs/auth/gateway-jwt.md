# Gateway JWT (branch `auth-2`)

Minimal multi-service auth demo that continues the monolith work on `auth`.

**Learn lesson (source of truth):**  
[`dev-sr.com/content/learn/dotnet-api/05-auth/03-gateway-jwt.mdx`](../../../../../dev-sr.com/content/learn/dotnet-api/05-auth/03-gateway-jwt.mdx)

## Layout

| Project | URL | Role |
| --- | --- | --- |
| `MyApp.Gateway` | `http://localhost:5100` | YARP — forwards `/auth/*`, `/products/*`, `/invoices/*` |
| `MyApp.Api` | `http://localhost:5101` | Token issuer + IAM + auth demos |
| `MyApp.ProductsApi` | `http://localhost:5102` | Thin products API + `AddMyAppAuth` |
| `MyApp.Auth.Jwt` | — | Shared HS256 + `PermissionPolicyProvider` (claim-only) |

## Run

```bash
# three terminals from MyApp/backend
dotnet run --project src/Services/MyApp.Api
dotnet run --project src/Services/MyApp.ProductsApi
dotnet run --project src/Services/MyApp.Gateway
```

`Jwt:Secret` / Issuer / Audience must match on AuthApi (`.env`) and ProductsApi (`appsettings.json`).

## Design notes

- Same claim shape as Learn 01/02: type `permission`, values `Permissions.*`.
- ProductsApi does **not** hit the IAM database — trusts validated JWT claims.
- AuthApi still runs `ConfigureJwtBearer` ban / `token_version` checks (issuer host only).
- Gateway does not validate JWT; each API host does.
