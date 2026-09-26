# Part 1 — Why results, and the building blocks

This part answers three questions before any HTTP or Mediator code appears:

1. Why throw *less*?
2. What do we put *instead* of a throw for everyday failures?
3. What tiny types do we need so the rest of the guide has something to stand on?

By the end you will understand `ErrorType`, `Error`, and `Success` in `Shared.Common`, and why they exist before `ErrorOr<T>` (part 2).

---

## 1. Big picture

### 1.1 The problem this whole guide solves

An HTTP API must turn two very different kinds of failure into a response:

1. **Expected failures** — bad input, missing resource, “not allowed”, business rule (“stock is zero”). These are *normal traffic*, not bugs.
2. **Unexpected failures** — database down, null reference, dependency timeout. These are *bugs or outages*. Log loudly; never leak internals to the client.

If you `throw` for both:

| What goes wrong | Why it hurts |
|---|---|
| Stack traces for routine 400/404s | CPU + log noise for something that happens every minute |
| Alerts cannot tell “user typed a bad id” from “DB is on fire” | Ops loses the signal that matters |
| Call sites forget to catch | One missed `try/catch` becomes a 500 for a simple not-found |

**Design used here:** expected failures are **values** (`ErrorOr<T>`). Unexpected failures still `throw`, and **one** global handler turns them into Problem Details (part 5).

That split is the single most important idea in this guide. Everything else is machinery to make it ergonomic.

### 1.2 Why a discriminated “either this or that” type

C# has no built-in `Result<T, E>`. The usual substitutes fail in different ways:

| Approach | What goes wrong |
|---|---|
| Return `null` / `false` | Caller has no *reason* to show the user or log |
| Return `(T? value, string? error)` | Easy to ignore the error half; no compiler pressure |
| Throw `NotFoundException` for every miss | Collapses expected and unexpected into one channel (see §1.1) |

We need a type that holds **either** a success value **or** one-or-more errors, forces the caller to handle both branches (`Match`), and converts cleanly from `T` / `Error` / `List<Error>` so handlers stay readable. That type is `ErrorOr<T>` (part 2).

### 1.3 Two channels, one rule

| Channel | Use for | Mechanism | Ends up as |
|---|---|---|---|
| **Result** (`ErrorOr<T>`) | Expected: validation, not found, conflict, forbidden, … | Returned as a value | RFC 9457 Problem Details via `ToProblem` (part 3) |
| **Exception** | Unexpected / infra that escapes the result channel | `throw` | Same Problem Details shape via one `IExceptionHandler` (part 5) |

Same *wire* shape for clients; different *source* for engineers. Clients always parse `application/problem+json` (plus normal 2xx bodies). Engineers know: if it was `ErrorOr`, it was expected; if it hit the exception handler as 500, something is wrong.

```
HTTP request
  → Carter endpoint     (IMediator.Send → MatchOk / MatchCreated / MatchNoContent)
  → Mediator pipeline   Logging → Validation → Caching → Transaction
      → Handler         returns ErrorOr<T>
        → EF / infra
  ← IResult             200 / 201 / 204, or application/problem+json
```

*Mediator:* MyApp uses a **hand-rolled** `Shared.Mediator` project, not the MediatR NuGet package. Same ideas (`IRequest`, `IRequestHandler`, `IPipelineBehavior`), with `ValueTask` and `AddPipelineBehavior(typeof(SomeBehavior<,>))`.

| Project | Responsibility |
|---|---|
| `Shared.Common` | **Errors:** `Error`, `ErrorOr<T>`, `ToProblem` / `Match*`. **Exceptions/Http:** `ExceptionMapping`, `ProblemDetailsExceptionHandler`, host registration helpers |
| `Shared.Application` | `ICommand`, `IQuery`, pipeline behaviors |
| `Shared.Mediator` | Dispatcher, registry, pipeline wrapping |
| `MyApp.Api` | Features, persistence, **host-only** registration of the shared handler |

**Packages you will see:** FluentValidation, EF Core, Carter, HybridCache, OpenApi / Scalar. **No** `ErrorOr` NuGet. `Shared.Common` references `Microsoft.AspNetCore.App` because it produces `IResult` / `ProblemDetails` — that is intentional so every future host can share one HTTP contract.

### 1.4 Design decisions (as implemented)

Memorize these; later parts are just consequences:

1. **Expected failures are data, never thrown.** Handlers return `ErrorOr<T>`; endpoints map once via `Match*`.
2. **Hand-written Result type** — reasons in §2.0.
3. **One exception handler** for unexpected / infra — not a chain of NotFound/Validation exception handlers.
4. **Validation returns every field error** as `List<Error>` → `ValidationProblemDetails`. Open-generic DI needs a small `ErrorOrFactory` (part 4).
5. **5xx never leak internals** outside Development.
6. **Both channels share the same Problem Details extensions** (`traceId`, `errorCode`, `errorType`).

---

## 2. The Result pattern — first bricks

### 2.0 Why hand-write it instead of the `ErrorOr` package

The NuGet package is fine for many apps. MyApp builds its own because:

| Reason | Detail |
|---|---|
| No name collisions | Our `Error` / `ErrorType` are ours; no clash with package types in usings |
| First-class `ServiceUnavailable` | Maps cleanly to 503 without string-prefix hacks |
| Multi-error lists are native | Validation needs several errors in one value |
| Narrow surface | Only the methods this codebase actually uses |

**Trade-off:** you own maintenance. For the small surface we use, that is the better fit — and it forces you to *understand* the type instead of treating a package as magic.

### 2.1 `ErrorType` — the vocabulary of expected failure

Before an `Error` value, we need a closed set of *kinds*. Each kind will later map to **exactly one** HTTP status (part 3). Putting the status on the enum itself would couple Common to HTTP numbers in the wrong place; the enum stays domain-ish, and `MapStatusCode` is the bridge.

```csharp
// Shared.Common/Errors/ErrorType.cs
public enum ErrorType
{
    Failure,             // generic client mistake → 400
    Validation,          // field rule failed → 400
    Unauthorized,        // not signed in → 401
    Forbidden,           // signed in but not allowed → 403
    NotFound,            // resource missing → 404
    Conflict,            // state clash / business rule → 409
    ServiceUnavailable,  // dependency down → 503
    Unexpected,          // “should never reach a client as a Result” → 500
}
```

**Why `Unexpected` exists on the Result channel at all:** occasionally code returns `Error.Unexpected(...)` instead of throwing (diagnostics, defensive empty-error-list handling). Part 3 still redacts the detail on 5xx. Prefer `throw` for true bugs so logs get a stack; use `Error.Unexpected` when you already have an `ErrorOr` in hand and must fail closed.

### 2.2 `Error` — one failure, with a stable code

An error without a **stable code** is useless for clients and for tests (`PRODUCT.NOT_FOUND` vs a free-form sentence). An error without a **human description** is useless for UI. So:

```csharp
// Shared.Common/Errors/Error.cs (shape)
public readonly struct Error
{
    public string Code { get; }          // machine-stable, e.g. PRODUCT.NOT_FOUND
    public string Description { get; }   // human-readable
    public ErrorType Type { get; }
    public IReadOnlyDictionary<string, object>? Metadata { get; }

    // private ctor + factories — you never `new Error(...)` from feature code
    public static Error NotFound(string code, string description, ...) { ... }
    public static Error Conflict(string code, string description, ...) { ... }
    public static Error ValidationField(string propertyName, string message) { ... }
    // Failure, Unauthorized, Forbidden, ServiceUnavailable, Unexpected, Validation, ...
}
```

**Why factories + private constructor?** So every error *must* pick a type. You cannot accidentally construct an `Error` with `Type = default` and wonder why it became a 500.

**Why `ValidationField(propertyName, message)`?** FluentValidation already gives you a property name. Using that string as `Code` lets part 3 group messages into `{ "Name": ["…"], "Price": ["…"] }` without a second mapping layer.

Open `Shared.Common/Errors/Error.cs` and skim the factories once — they are thin wrappers around the private ctor.

### 2.3 Domain error catalogue — *optional later*

MyApp has **no Domain project yet**. Feature handlers use codes like `PRODUCT.NOT_FOUND` next to the feature. When you add aggregates, a small `DomainError` helper (stable `DOMAIN.*` codes) keeps invariant messages consistent. That is **Illustrative** today — do not invent a Domain layer just for this guide.

### 2.4 Void success — one marker: `Success`

Commands sometimes have nothing useful to return (update / delete). People invent empty `Created` / `Updated` / `Deleted` types. That adds ceremony the HTTP layer does not need: clients only see status + body.

MyApp uses a single marker:

```csharp
// Shared.Common/Errors/ErrorOr.cs
public readonly record struct Success;
```

| Command intent | Return type | HTTP helper (part 3) |
|---|---|---|
| Create that returns an id | `ErrorOr<Guid>` | `MatchCreated` → 201 + Location |
| Query / update that returns a DTO | `ErrorOr<TDto>` | `MatchOk` → 200 + body |
| Update / delete with no body | `ErrorOr<Success>` | `MatchNoContent` → 204 |

`ICommand` (no type arg) is defined as `IRequest<ErrorOr<Success>>` in Application — part 2.

**What would go wrong with Created/Updated/Deleted types?** More types to import, no extra HTTP meaning, and handlers that only swap markers without changing behavior. Keep void success boring.

---

## Checkpoint

You should now be able to explain, without looking:

- Why expected failures should not be exceptions.
- What `ErrorType` is for.
- Why every `Error` has a `Code`.
- Why void commands use `Success` instead of three empty structs.

**Next:** [part2.md](part2.md) — put value and errors into one type (`ErrorOr<T>`), wire commands/queries, and walk a real Product handler.
