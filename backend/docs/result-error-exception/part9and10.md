# Parts 9–10 — Testing the design, then building it yourself

Parts 1–5 taught the *why* and the *how*. This part teaches you how to **prove** it. If you only skim tables, you will miss *why* each test layer exists — read the narrative first, use the matrix as a checklist second.

Pagination and DDD are **not implemented** in MyApp yet. Those rows stay in a “deferred” section so you do not chase green tests for code that does not exist.

---

## 9. Testing every edge case

### 9.0 Why three layers?

| Layer | Project | What it catches | What it does *not* need |
|---|---|---|---|
| **Unit** | `MyApp.Tests.Unit` | Wrong status mapping, redaction bugs, `IsExpected` mistakes | A web host |
| **Behavior** | `MyApp.Tests.Unit` | Validation not short-circuiting, DI-shaped behavior regressions | HTTP |
| **Integration** | `MyApp.Tests.Integration` | Routing, Carter, real DI, Problem Details on the wire, Production gate | Mocking `ToProblem` |

A pure unit test will not catch “diag routes still registered in Production.” An integration test is a slow way to debug `MapStatusCode`. Use both.

```
Error / Exception  →  Unit (mapping)
ValidationBehavior →  Behavior (fake next)
Full HTTP path     →  Integration (WebApplicationFactory + /_diag + Products)
```

---

### 9.1 Why diagnostic endpoints?

Product handlers only exercise a *subset* of error kinds (validation, not-found, success). You still need a controlled way to force:

- every `ErrorType`
- mixed validation + forbidden
- `Error.Unexpected` with a secret string (leak test)
- thrown `ArgumentException` / unexpected

Without diagnostics you either pollute Product features with test-only branches or accept blind spots.

**Carter module:** `MyApp.Api/Features/Diagnostics/ErrorDiagnosticsEndpoint.cs`

```
GET  /_diag/result/{kind}
GET  /_diag/throw/{kind}
POST /_diag/body
```

**Rules that matter for learning:**

1. Registered only when `IsDevelopment()` **or** `IsEnvironment("Testing")`. Production must 404.
2. `.ExcludeFromDescription()` — Scalar/OpenAPI stay clean for real features.
3. `unexpected` **result** uses description `"secret internal detail"` so leak assertions are meaningful.
4. No domain / cancel throw kinds — keep the diag surface small.

Try it locally (Development):

```bash
curl -i localhost:5089/_diag/result/validation-multi
curl -i localhost:5089/_diag/throw/unexpected
curl -i -X POST localhost:5089/_diag/body \
  -H 'Content-Type: application/json' -d '{bad json'
```

#### Result kinds → what you are learning

| `kind` | Expected | Lesson |
|---|---|---|
| `ok` | 200 | Success path of `MatchOk` |
| `validation` | 400 + field map | Single validation error |
| `validation-multi` | 400, grouped fields | List overload grouping |
| `mixed` | **403** | Non-validation wins (part 3) |
| `not-found` / `conflict` / `unauthorized` / `forbidden` | 404 / 409 / 401 / 403 | `MapStatusCode` |
| `unavailable` | 503 | `ServiceUnavailable` |
| `failure` | 400 | `ErrorType.Failure` ≠ Validation |
| `unexpected` | 500, **no** secret text | 5xx redaction on Result channel |

#### Throw kinds → what you are learning

| `kind` | Expected | Lesson |
|---|---|---|
| `unexpected` | 500, generic detail outside Dev | Exception handler redaction |
| `argument` | 500 | Programmer error ≠ 400 |

---

### 9.2 Edge-case matrix

Use this as a **coverage checklist**, not as the only documentation.

#### Implemented now (must have tests)

| Area | Input | Expected |
|---|---|---|
| Result mapping | all kinds in §9.1 | status + `application/problem+json` + `traceId` (errors) |
| | `mixed` | **403**, not 400 |
| | `unexpected` | no `secret internal detail` |
| Exceptions | throw kinds above | as table |
| Framework | unknown route | 404 with a body (`UseStatusCodePages`) |
| | wrong verb on diag GET | 405 with a body |
| | malformed JSON on `POST /_diag/body` | 400, not 500 |
| | wrong `Content-Type` on body | 415 or 400 with a body |
| Feature path | Product create validation / get 404 / update+delete 204 | real handler path |
| Gate | Production host | `/_diag/result/ok` → 404 |

#### Deferred (do not invent tests yet)

| Area | Notes |
|---|---|
| Pagination | `PageRequest`, `ToPagedAsync`, allow-listed `orderBy` — future part 6 |
| DDD | `DomainError`, `DomainException`, business-rule throw kinds |
| EF concurrency mapping | Prefer `Error.Conflict` or host-local handling if needed — no Common `ConcurrencyException` |
| Abort/cancel HTTP | Fragile in integration; unit-test `IsExpected(OperationCanceledException)` only |
| Manual log/metric checklist | §9.7 |

---

### 9.3 Unit tests — pin pure mapping

**Project:** `tests/MyApp.Tests.Unit` → references `Shared.Common` (+ Application for behavior).

**What to write (and why):**

1. **Theory over every `ErrorType` → `MapStatusCode`**  
   If someone “fixes” Conflict to 400, CI fails immediately.

2. **`ToProblem` facts**  
   - Validation list groups by field.  
   - Mixed list → 403.  
   - Unexpected detail redacted.  
   - Empty list → 500 / `EMPTY_ERROR_LIST`.  
   Execute `IResult` against a `DefaultHttpContext` with `RequestServices` configured for JSON (see existing `ErrorMappingTests`).

3. **`ExceptionMapping` facts**  
   Bad request 400; argument / invalid operation 500; `IsExpected` true for cancel/bad-request, false for unexpected.

These tests never start Kestrel. They are the fastest feedback when you edit Common.

---

### 9.4 Behavior tests — prove the pipeline, not just HTTP

Still in `MyApp.Tests.Unit`. Construct `ValidationBehavior<ProbeRequest, ErrorOr<string>>` with a real FluentValidation validator and a fake `next`:

```csharp
RequestHandlerDelegate<ErrorOr<string>> next = ct =>
{
    called = true;
    return ValueTask.FromResult<ErrorOr<string>>("ok");
};
```

| Case | Assert |
|---|---|
| Invalid request | `IsError`, `called == false`, validation errors present |
| Valid request | `called == true`, value `"ok"` |
| No validators | passthrough even if the request looks empty |

**Why this matters:** part 4’s DI trap can make HTTP “look fine” while validation never ran for some requests. A behavior test with a fake `next` proves short-circuit regardless of Carter.

Remember: custom Mediator’s `next` takes `CancellationToken`.

---

### 9.5 Pagination tests — deferred

When paging lands, add InMemory `ToPagedAsync` tests and HTTP paging validation. Not part of the current MyApp surface.

---

### 9.6 Integration tests — the whole HTTP path

**Project:** `MyApp.Tests.Integration`

**`MyAppFactory`:** `WebApplicationFactory<Program>` with environment **`Testing`**, SQLite stripped, InMemory EF. Because env is Testing, `/_diag/*` **is** registered — that is intentional for tests.

**`ProductionMyAppFactory`:** same DB swap, environment **`Production`**. Used only to prove diag routes are absent (404).

What the suite should teach you when you read it:

1. **Theory** over `/_diag/result/{kind}` and `/_diag/throw/{kind}` — status, problem JSON, `traceId`.
2. **Facts** for validation-multi field counts, mixed→403, secret leak bans, malformed JSON, wrong content-type, wrong method.
3. **Fact** Production hides diag.
4. **Product facts** — create/update/delete validation + 404 + 204. Diagnostics prove mapping; Product proves the *real* feature path still uses the same channel.

`public partial class Program;` at the bottom of `Program.cs` enables `WebApplicationFactory<Program>`.

Run:

```bash
dotnet test tests/MyApp.Tests.Unit
dotnet test tests/MyApp.Tests.Integration
```

---

### 9.7 Log and metric checks (manual, once)

`SuppressDiagnosticsCallback` is easy to get wrong without looking at logs:

1. `/_diag/throw/unexpected` → Error-level unhandled diagnostics **kept** (real incident).
2. Malformed JSON / `BadHttpRequestException` → Warning path; Error-level unhandled diagnostics **suppressed** (`IsExpected`).
3. `/_diag/result/unexpected` → **no** exception logs (`ErrorOr` never throws).

Do this once when you change `ExceptionMapping.IsExpected` or the handler.

---

## 10. Build-it-yourself checklist

Work top to bottom. After each section, you should be able to explain *why* — not only that the file exists.

### Done in current MyApp (parts 1–5 + 9)

1. **Common — vocabulary**  
   [ ] `ErrorType` enum with clear HTTP intent  
   [ ] `Error` readonly struct + factories (`NotFound`, `ValidationField`, …)  
   [ ] `Success` marker; no Created/Updated/Deleted clutter  

2. **Common — Result type**  
   [ ] `ErrorOr<T>` with implicits, `Match`, `Then`  
   [ ] `ErrorOrFactory` for open-generic behaviors  

3. **Common — HTTP (Result channel)**  
   [ ] `ErrorExtensions.ToProblem` (single + list, mixed-wins, 5xx redact)  
   [ ] `ErrorOrExtensions.MatchOk` / `MatchCreated` / `MatchNoContent`  

4. **Common — HTTP (Exception channel)** under `Shared.Common/Exceptions/Http/`  
   [ ] `ExceptionMapping` + `IsExpected`  
   [ ] `ProblemDetailsExceptionHandler` (`IExceptionHandler`)  
   [ ] `AddSharedExceptionHandling` / `UseSharedExceptionHandling`  
   [ ] No typed concurrency exception in Common — conflicts via `Error.Conflict` when needed  

5. **Application**  
   [ ] `ICommand` / `ICommand<T>` / `IQuery<T>` → `ErrorOr<…>`  
   [ ] `ValidationBehavior<TRequest, TResponse>` + `ErrorOrFactory` (not `ErrorOr<TResult>` in the interface)  
   [ ] Logging / Caching / Transaction behaviors registered in order  

6. **Host + features**  
   [ ] Presentation registers shared exception handling  
   [ ] Carter Product CRUD; void mutations use `MatchNoContent`  
   [ ] Validators discovered from assembly  
   [ ] Diagnostics module gated to Development/Testing  

7. **Tests**  
   [ ] Unit: mapping + exception mapping + validation behavior  
   [ ] Integration: diag matrix + Product path + Production gate  

### Future (when you implement them)

8. **Paging** — `PageRequest`, `PagedResult`, `ToPagedAsync`, list validators, §9.5 tests  
9. **DDD (optional)** — `DomainException` / `DomainError`; translate at handler boundary (part 5 §5.4)

---

## Where to go next

- Re-read [part1.md](part1.md) §1.3 if the two-channel idea still feels fuzzy.  
- Change a `MapStatusCode` mapping on purpose and watch unit tests fail — that is the feedback loop this design wants.  
- Prefer Problem Details over inventing a `{ success, data }` envelope unless you have a hard client constraint (see part 3).
