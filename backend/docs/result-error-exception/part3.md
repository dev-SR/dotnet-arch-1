# Part 3 — From Result to HTTP

Parts 1–2 built the in-memory `ErrorOr<T>` story. This part bridges that C# value to an actual HTTP response. If an API response looks wrong in the browser or Scalar, the mapping logic lives here.

## 1. Architecture: The Two-File Split

All Result → HTTP mapping is intentionally split into two static extension classes under `Shared.Common/Errors/Http/`.

```text
Handler / ValidationBehavior
        │
        ▼
   ErrorOr<T>          ← C# value (Part 2)
        │
        ▼
┌───────────────────────┐
│  ErrorOrExtensions    │  THE ROUTER: Branches Success vs. Failure
│  MatchOk / Created /  │  Success → 200 / 201 / 204
│  NoContent            │  Failure → Hand off to ErrorExtensions
└───────────┬───────────┘
            │ errors.ToProblem(ctx)
            ▼
┌───────────────────────┐
│  ErrorExtensions      │  THE RENDERER: Builds Problem Details
│  ToProblem / MapCode  │  Maps ErrorType → HTTP Status
│  Decorate             │  Formats application/problem+json
└───────────────────────┘
            │
            ▼
     bytes on the wire
```

**Why two files?**
* **Separation of Concerns:** Endpoints shouldn't mix "pick success status" with "build error JSON".
* **Testability:** Diagnostics and unit tests can test error rendering without dragging in `ErrorOr` routing logic.
* **Consistency:** Changing validation grouping won't accidentally break success routing.

## 2. Wire Format Philosophy

MyApp rejects custom envelopes like `{ success: true, data: ... }`. We rely on industry standards:
* **Success:** Standard HTTP status codes + normal JSON bodies.
* **Failure:** **RFC 9457 Problem Details** (`application/problem+json`). Clients branch on HTTP status; the body provides context.
* **Standard Extensions:** Every error body includes `traceId` (for log correlation), `errorCode` (machine-readable switch), and `errorType` (debug info).

---

## 3. The Renderer: `ErrorExtensions.cs`

This file turns `Error` or `List<Error>` into standards-compliant HTTP error responses. It does *not* decide 200 vs 201 vs 204.

```csharp
using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Shared.Common.Errors.Http;

public static class ErrorExtensions
{
    private const string ProblemJson = "application/problem+json";

    // Single Error → ProblemDetails
    public static IResult ToProblem(this Error error, HttpContext ctx, string? traceId = null)
    {
        var status = (int)MapStatusCode(error);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            // SECURITY: Never echo internal descriptions on 5xx to prevent leaking secrets.
            Detail = status >= 500 ? "An unexpected error occurred." : error.Description,
            Type = $"https://httpstatuses.io/{status}",
            Instance = ctx.Request.Path,
        };
        Decorate(problem, ctx, traceId, error.Code, error.Type.ToString());

        // TypedResults ensures compile-time guarantees and better OpenAPI generation.
        return TypedResults.Json(problem, statusCode: status, contentType: ProblemJson);
    }

    // List of Errors → ProblemDetails
    public static IResult ToProblem(this IReadOnlyList<Error> errors, HttpContext ctx, string? traceId = null)
    {
        if (errors.Count == 0)
            return Error.Unexpected("EMPTY_ERROR_LIST", "An ErrorOr failure carried no errors.")
                .ToProblem(ctx, traceId);

        // RULE: Non-validation errors win. (e.g., 403 Forbidden overrides field validation).
        var nonValidation = errors.FirstOrDefault(e => e.Type != ErrorType.Validation);
        if (!nonValidation.Equals(default(Error))) return nonValidation.ToProblem(ctx, traceId);

        // RULE: Group validation errors by field (Code) for the client.
        var map = errors.GroupBy(e => e.Code)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        var problem = new ValidationProblemDetails(map)
        {
            Status = 400,
            Title = "One or more validation errors occurred.",
            Type = "https://httpstatuses.io/400",
            Instance = ctx.Request.Path,
        };
        Decorate(problem, ctx, traceId, "VALIDATION", nameof(ErrorType.Validation));
        return TypedResults.Json(problem, statusCode: 400, contentType: ProblemJson);
    }

    private static void Decorate(ProblemDetails p, HttpContext ctx, string? traceId, string code, string type)
    {
        p.Extensions["traceId"] = traceId ?? ctx.TraceIdentifier;
        p.Extensions["errorCode"] = code;
        p.Extensions["errorType"] = type;
    }

    // The single source of truth for ErrorType → HTTP Status mapping.
    public static HttpStatusCode MapStatusCode(Error error) => error.Type switch
    {
        ErrorType.Failure or ErrorType.Validation => HttpStatusCode.BadRequest,
        ErrorType.Unauthorized                    => HttpStatusCode.Unauthorized,
        ErrorType.Forbidden                       => HttpStatusCode.Forbidden,
        ErrorType.NotFound                        => HttpStatusCode.NotFound,
        ErrorType.Conflict                        => HttpStatusCode.Conflict,
        ErrorType.ServiceUnavailable              => HttpStatusCode.ServiceUnavailable,
        ErrorType.Unexpected                      => HttpStatusCode.InternalServerError,
        _                                         => HttpStatusCode.InternalServerError,
    };
}
```

### Key Rendering Rules
1. **`MapStatusCode`:** The *only* bridge between domain vocabulary (`ErrorType`) and HTTP numbers.
2. **5xx Redaction:** If status is ≥ 500, the `Detail` field is hardcoded. Even if a developer accidentally returns `Error.Unexpected(..., "secret connection string")`, the client never sees it.
3. **Mixed Error Priority:** If a request fails validation *and* authorization, the authorization error (e.g., 403) wins. A forbidden user shouldn't be coached on field formatting.
4. **Validation Grouping:** Multiple validation errors are grouped by `Error.Code` into ASP.NET's native `ValidationProblemDetails` dictionary.

---

## 4. The Router: `ErrorOrExtensions.cs`

This file gives Carter endpoints a one-liner to handle `ErrorOr<T>`. It branches on success vs. failure; on failure, it strictly delegates to `ErrorExtensions`.

```csharp
using Microsoft.AspNetCore.Http;

namespace Shared.Common.Errors.Http;

public static class ErrorOrExtensions
{
    // THE HINGE: Every public helper routes through this private method.
    private static IResult Match<T>(this ErrorOr<T> r, HttpContext ctx,
        Func<T, IResult> onSuccess, string? traceId = null)
        => r.Match(onValue: onSuccess, onError: errors => errors.ToProblem(ctx, traceId));

    public static IResult MatchOk<T>(this ErrorOr<T> r, HttpContext ctx, string? traceId = null)
        => r.Match(ctx, TypedResults.Ok, traceId);

    public static IResult MatchCreated<T>(this ErrorOr<T> r, HttpContext ctx,
        Func<T, string> locationSelector, string? traceId = null)
        => r.Match(ctx, v => TypedResults.Created(locationSelector(v), v), traceId);

    public static IResult MatchNoContent(this ErrorOr<Success> r, HttpContext ctx, string? traceId = null)
        => r.Match(ctx, _ => TypedResults.NoContent(), traceId);

    public static IResult ToResult<T>(this ErrorOr<T> r, HttpContext ctx, string? traceId = null)
        => !r.IsError ? TypedResults.Ok(r.Value) : r.Errors.ToProblem(ctx, traceId);
}
```

### Key Routing Rules
* **The Private `Match` Hinge:** This guarantees that *every* endpoint using these helpers gets the exact same Problem Details shape and multi-field validation handling on failure.
* **`HttpContext` Requirement:** Passed explicitly so the renderer can access `ctx.TraceIdentifier` and `ctx.Request.Path` without relying on ambient context.
* **Helper Selection:**
    * `MatchOk`: Queries / Updates returning a body (200).
    * `MatchCreated`: Creates returning an ID/Resource (201 + Location).
    * `MatchNoContent`: Void mutations constrained to `ErrorOr<Success>` (204).

---

## 5. Usage in Carter Endpoints

Endpoints follow a strict **Send → Match** pattern. You never write `if (result.IsError)` or `try/catch` for expected domain failures.

```csharp
public class CreateProductEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/products", HandleAsync).WithName("CreateProduct");
    }

    private static async Task<IResult> HandleAsync(
        CreateProductCommand command, IMediator mediator, HttpContext http, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);

        // One line handles success (201) AND failure (400/500 Problem Details)
        return result.MatchCreated(http, id => $"/products/{id}");
    }
}
```

### End-to-End Flows

* **404 Not Found:** Handler returns `Error.NotFound` → `MatchOk` routes to `onError` → `ToProblem` maps to `404` + Problem Details.
* **201 Created:** Handler returns `Guid` → `MatchCreated` routes to `onValue` → `201` + `Location` + JSON body.
* **400 Validation:** Pipeline intercepts invalid request → returns multiple `Validation` errors → Handler *never runs* → `MatchCreated` routes to `onError` → `ToProblem` groups by field into `ValidationProblemDetails`.

---

## Checkpoint

* [ ] You understand the split: **`ErrorOrExtensions`** branches success/failure; **`ErrorExtensions`** renders Problem Details.
* [ ] You know why 5xx errors redact their descriptions (security).
* [ ] You know that in a mixed error list, non-validation errors (like Forbidden) override validation errors.
* [ ] You can trace a Carter endpoint's `MatchCreated` call down to the JSON wire format.

**Next:** [part4.md](part4.md) — Validation as part of the result channel (and the DI trap that silently disables it).
