# Part 4 — Validation as part of the Result channel

Parts 1–3 covered expected failures as `ErrorOr` values and how endpoints turn them into HTTP. This part answers: **where do field-validation failures come from before the handler even runs?**

Invalid requests are **expected** failures. They stay on the Result channel — no `try/catch`, no `ValidationException`. FluentValidation + one Mediator pipeline behavior produce `List<Error>`; part 3’s `Match*` / `ToProblem` already know how to render them.

If a request should have been rejected for bad fields but still hit the database, start here — not in the handler.

---

## 1. Architecture: what these pieces are for

Validation is split across three concerns on purpose:

```text
HTTP body → CreateProductCommand
        │
        ▼
┌───────────────────────────┐
│  CreateProductValidator   │  THE RULES: field constraints (FluentValidation)
│  AbstractValidator<T>     │  Discovered by AddValidatorsFromAssembly
└───────────┬───────────────┘
            │ injected as IEnumerable<IValidator<TRequest>>
            ▼
┌───────────────────────────┐
│  ValidationBehavior       │  THE GATE: run all validators before the handler
│  IPipelineBehavior<,>     │  Fail → ErrorOr failure; success → next(...)
└───────────┬───────────────┘
            │ needs ErrorOr<T> but only knows TResponse
            ▼
┌───────────────────────────┐
│  ErrorOrFactory           │  THE ADAPTER: List<Error> → TResponse (ErrorOr<T>)
│  Failure<TResponse>(...)  │  Open-generic DI cannot express ErrorOr<> in the signature
└───────────┬───────────────┘
            ▼
   ErrorOr<Guid> failure  →  endpoint MatchCreated → ToProblem (part 3)
```

| File | What it is for | What it is *not* for |
|---|---|---|
| Feature `*Validator.cs` | Declare field rules for one command/query | HTTP status codes, Problem Details |
| [`ValidationBehavior.cs`](../../src/Shared/Application/Behaviors/ValidationBehavior.cs) | Short-circuit the pipeline when rules fail; map FV failures → `Error.ValidationField` | Choosing 400 vs 403; writing JSON |
| [`ErrorOrFactory.cs`](../../src/Shared/Common/Errors/ErrorOrFactory.cs) | Build `ErrorOr<T>` when the behavior only has `TResponse` | Everyday handler returns (handlers use implicits) |

**Pipeline order** (registration in `MyApp.Api/Application/DependencyInjection.cs`):

```text
Logging → Validation → Caching → Transaction → Handler
```

Validation fails cheaply before a transaction or cache logic that assumes a valid request.

---

## 2. Why a pipeline behavior (not validation inside every handler)?

You *could* do this in every handler:

```csharp
var validation = await validator.ValidateAsync(command, ct);
if (!validation.IsValid) return /* map to errors */;
```

That works once. Then every handler copies boilerplate, forgets a validator, or maps failures differently.

**Pipeline behaviors** wrap *every* `IRequest`. One `ValidationBehavior` + one validator per command keeps handlers focused on persistence / domain decisions (e.g. not-found).

---

## 3. The open-generic DI trap (read this twice)

`AddPipelineBehavior(typeof(ValidationBehavior<,>))` registers:

```csharp
services.AddScoped(typeof(IPipelineBehavior<,>), openGenericBehaviorType);
```

When Mediator resolves behaviors for `CreateProductCommand`, it asks for:

```csharp
IPipelineBehavior<CreateProductCommand, ErrorOr<Guid>>
```

because `ICommand<Guid> : IRequest<ErrorOr<Guid>>` — **`TResponse` is already `ErrorOr<Guid>`**.

### Broken approach (do not use)

```csharp
// WRONG
class ValidationBehavior<TRequest, TResult>
    : IPipelineBehavior<TRequest, ErrorOr<TResult>>
```

DI fills type parameters **positionally**. For `IPipelineBehavior<CreateProductCommand, ErrorOr<Guid>>` it sets:

- `TRequest = CreateProductCommand`
- `TResult = ErrorOr<Guid>`

So the closed type becomes `IPipelineBehavior<…, ErrorOr<ErrorOr<Guid>>>` — **never** the service that was requested. The behavior is simply **not in the pipeline**. Unit tests of the class can still pass; HTTP requests silently skip validation.

### Working approach (as implemented)

```csharp
class ValidationBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
```

Here `TResponse` *is* `ErrorOr<Guid>` at runtime. On failure the behavior must return that exact type — hence `ErrorOrFactory`.

---

## 4. Full code — `ValidationBehavior.cs`

**Reminder (§1):** this file is the gate. It does not write HTTP; it returns an `ErrorOr` failure so part 3 can render it.

Live path: `Shared.Application/Behaviors/ValidationBehavior.cs`.

```csharp
using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using Shared.Common.Errors;

namespace Shared.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var list = validators as IValidator<TRequest>[] ?? validators.ToArray();
        if (list.Length == 0)
            return await next(cancellationToken);

        var failures = (await Task.WhenAll(list.Select(v => v.ValidateAsync(request, cancellationToken))))
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count == 0)
            return await next(cancellationToken);

        logger.LogWarning(
            "Validation failed for {Request}: {Count} error(s)",
            typeof(TRequest).Name,
            failures.Count);

        var errors = failures
            .Select(f => Error.ValidationField(f.PropertyName, f.ErrorMessage))
            .ToList();

        // TResponse is ErrorOr<T> at runtime; open-generic DI can't express that in the signature.
        return ErrorOrFactory.Failure<TResponse>(errors);
    }
}
```

### How it works (step by step)

1. **No validators registered** for this `TRequest` → call `next` (passthrough). Queries without a validator are fine.
2. **Run all validators in parallel** → flatten FluentValidation `ValidationFailure`s.
3. **Zero failures** → call `next` → handler runs.
4. **Any failures** → log a warning → map each to `Error.ValidationField(propertyName, message)` → **do not call `next`** → return `ErrorOrFactory.Failure<TResponse>(errors)`.

### Details for *our* Mediator (not MediatR)

| Detail | Why it matters |
|---|---|
| `ValueTask<TResponse>` | Matches `Shared.Mediator` contracts |
| `next(cancellationToken)` | Token is on the delegate — do not call parameterless `next()` |
| `Error.ValidationField` | `Code` = property name → part 3 groups into `errors` map |

Unit tests in `MyApp.Tests.Unit/ValidationBehaviorTests` pin: invalid skips `next`, valid reaches `next`, no validators passthrough.

---

## 5. Full code — `ErrorOrFactory.cs`

**Reminder (§1):** handlers that know `T` can `return errors;` (implicit conversion). The behavior only knows `TResponse`, so it needs this factory.

Live path: `Shared.Common/Errors/ErrorOrFactory.cs`.

```csharp
using System.Collections.Concurrent;
using System.Reflection;

namespace Shared.Common.Errors;

/// <summary>
/// Builds <see cref="ErrorOr{T}"/> failure values when the concrete T is only known as
/// <c>TResponse</c> (open-generic pipeline behaviors). Prefer returning <c>Error</c> /
/// <c>List&lt;Error&gt;</c> directly from handlers where T is known.
/// </summary>
public static class ErrorOrFactory
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> Cache = new();

    public static TResponse Failure<TResponse>(List<Error> errors)
    {
        var method = Cache.GetOrAdd(typeof(TResponse), static responseType =>
        {
            if (!responseType.IsGenericType
                || responseType.GetGenericTypeDefinition() != typeof(ErrorOr<>))
            {
                throw new InvalidOperationException(
                    $"{responseType.Name} is not ErrorOr<>. Handlers should return ErrorOr<T>.");
            }

            return typeof(ErrorOrFactory)
                .GetMethod(nameof(Create), BindingFlags.NonPublic | BindingFlags.Static)!
                .MakeGenericMethod(responseType.GetGenericArguments()[0]);
        });

        return (TResponse)method.Invoke(null, [errors])!;
    }

    private static ErrorOr<T> Create<T>(List<Error> errors) => ErrorOr<T>.From(errors);
}
```

### How it works

1. Look up (or build) a cached `MethodInfo` for `Create<T>` where `T` is the type argument inside `ErrorOr<T>`.
2. If `TResponse` is not `ErrorOr<>`, throw — your `ICommand` / `IQuery` contracts were violated.
3. Invoke `ErrorOr<T>.From(errors)` and cast back to `TResponse`.

Reflection lives **once**, cached — not sprinkled through the behavior.

---

## 6. A validator — as in MyApp

```csharp
// Features/Products/CreateProduct/CreateProductValidator.cs
using FluentValidation;

namespace MyApp.Features.Products.CreateProduct;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).MinimumLength(3).MaximumLength(100);
        RuleFor(x => x.Category).MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
    }
}
```

Registration (`Application/DependencyInjection.cs`):

```csharp
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
services.AddPipelineBehavior(typeof(CachingBehavior<,>));
services.AddPipelineBehavior(typeof(TransactionBehavior<,>));

services.AddValidatorsFromAssembly(
    typeof(ApplicationServiceCollectionExtensions).Assembly,
    includeInternalTypes: true);
```

**Handler stays free of field rules.** It assumes a valid command.

---

## 7. What the HTTP response looks like

After `ValidationBehavior` returns an `ErrorOr` failure, the endpoint still calls `MatchCreated` / `MatchOk` / … Failure → `ToProblem(list)` (part 3):

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/products",
  "errors": {
    "Name": ["'Name' must be between 3 and 100 characters."],
    "Price": ["'Price' must be greater than '0'."]
  },
  "traceId": "0HN7…",
  "errorCode": "VALIDATION",
  "errorType": "Validation"
}
```

Field keys are FluentValidation `PropertyName`. For camelCase on the wire, use `.OverridePropertyName("name")` on rules.

### End-to-end — invalid create

```text
POST /products  { "name": "ab", "price": 0, ... }
  → ValidationBehavior collects failures
  → Error.ValidationField("Name", …), Error.ValidationField("Price", …)
  → ErrorOrFactory.Failure → ErrorOr<Guid> with Validation errors
  → CreateProductCommandHandler NEVER runs
  → MatchCreated → errors.ToProblem → 400 ValidationProblemDetails
```

No `try/catch`. Same Result channel as `Error.NotFound`.

---

## Checkpoint

* [ ] You can explain **what each piece is for**: validator = rules, behavior = gate, factory = open-generic adapter.
* [ ] You can explain the `ErrorOr<ErrorOr<T>>` DI trap and the unconstrained `TResponse` fix.
* [ ] You know why `ErrorOrFactory` exists and when handlers do *not* need it.
* [ ] You can predict the JSON for a multi-field failure and that the handler never ran.

**Next:** [part5.md](part5.md) — what still throws, and the one shared handler that maps it.
