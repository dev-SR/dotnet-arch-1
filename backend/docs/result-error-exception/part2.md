# Part 2 — `ErrorOr<T>`, contracts, and a real handler

Part 1 gave you `Error` and `Success`. This part builds the container that holds **either** a value **or** errors, locks every command/query into that shape, and shows a complete GetProduct path.

---

## 2.5 `ErrorOr<T>` — the type itself

### What it is for

`ErrorOr<T>` is a discriminated union:

- **Success:** `_value` is set, `_errors` is null → `IsError == false`
- **Failure:** `_errors` is a non-null list → `IsError == true` (value is meaningless)

Never both. Never “success with a warning list.” If you need warnings later, that is a different design.

### Why several errors, not one?

FluentValidation can fail `Name` *and* `Price` on the same request. Returning only the first error forces the client into a fix-one-resubmit loop. A `List<Error>` lets part 3 emit one `ValidationProblemDetails` with every field.

### Why implicit conversions?

Handlers should read like prose:

```csharp
return productDto;   // becomes ErrorOr<ProductDto> success
return Error.NotFound("PRODUCT.NOT_FOUND", $"Product '{id}' was not found.");
return errors;       // List<Error> → failure
```

Without implicits you write `ErrorOr<ProductDto>.From(...)` everywhere and the type becomes noise.

### Why `.Value` throws when `IsError`?

Accessing `.Value` on a failure is a programmer mistake. Failing loudly is better than returning `default` and shipping a silent bug. Prefer `.Match(...)` so both branches are required by the compiler.

### The shape (matches `Shared.Common/Errors/ErrorOr.cs`)

```csharp
public readonly record struct Success;

public readonly struct ErrorOr<TValue>
{
    private readonly TValue? _value;
    private readonly List<Error>? _errors;

    public bool IsError => _errors is not null;

    public TValue Value => IsError
        ? throw new InvalidOperationException(
            "Cannot access Value on a failed ErrorOr. Check IsError first, or use Match.")
        : _value!;

    public IReadOnlyList<Error> Errors => _errors ?? [];

    public Error FirstError => /* first or throw */;

    public static ErrorOr<TValue> From(List<Error> errors) => new(errors);

    public static implicit operator ErrorOr<TValue>(TValue value) => new(value);
    public static implicit operator ErrorOr<TValue>(Error error) => new([error]);
    public static implicit operator ErrorOr<TValue>(List<Error> errors) => From(errors);

    public TResult Match<TResult>(
        Func<TValue, TResult> onValue,
        Func<IReadOnlyList<Error>, TResult> onError) =>
        IsError ? onError(Errors) : onValue(Value);

    // Then / ThenAsync: run the next fallible step only on success
    public ErrorOr<TNext> Then<TNext>(Func<TValue, ErrorOr<TNext>> next) =>
        IsError ? Errors.ToList() : next(Value);
}
```

### `Match` vs `if (IsError)`

```csharp
// Easy to forget the error branch, or read Value too early
if (result.IsError) return result.Errors.ToProblem(ctx);
return Results.Ok(result.Value);

// Match forces both branches; return types must agree
return result.Match(
    value => Results.Ok(value),
    errors => errors.ToProblem(ctx));
```

Part 3’s `MatchOk` / `MatchCreated` / `MatchNoContent` are thin wrappers around this idea so endpoints do not repeat the failure branch.

### `Then` — composing steps without nested ifs

When you later add factory methods that return `ErrorOr<Product>`:

```csharp
return await Product.Create(name, price)
    .ThenAsync(async p =>
    {
        await db.Products.AddAsync(p, ct);
        await db.SaveChangesAsync(ct);
        return p.Id; // ErrorOr<Guid>
    });
```

If `Create` failed, `ThenAsync` never runs the lambda — the same errors propagate. Today’s Product handlers are simpler (load or create inline); keep `Then` in mind when steps multiply.

### `ErrorOrFactory` (preview — part 4)

Pipeline behaviors only know `TResponse`, not `T` inside `ErrorOr<T>`. Handlers can `return errors;` via implicit conversion. Behaviors call `ErrorOrFactory.Failure<TResponse>(errors)` once. Details and the DI trap live in part 4 — you only need to know *why* the factory exists: open-generic pipelines cannot express `ErrorOr<>` in their interface type args cleanly.

---

## 2.6 Command and query contracts

Without a convention, one handler returns `ProductDto`, another returns `ErrorOr<ProductDto>`, another throws. Behaviors and endpoints cannot assume a shape.

MyApp locks every application request into `ErrorOr`:

```csharp
// Shared.Application/Abstractions/Commands/ICommand.cs
public interface ICommand<TResponse> : IRequest<ErrorOr<TResponse>> { }
public interface ICommand : IRequest<ErrorOr<Success>> { }

// Shared.Application/Abstractions/Queries/IQuery.cs
public interface IQuery<TResponse> : IRequest<ErrorOr<TResponse>> { }
```

`IRequest<>` comes from `Shared.Mediator`.

| Interface | Meaning |
|---|---|
| `ICommand<Guid>` | Mutation that returns a payload (e.g. new id) |
| `ICommand` | Mutation with no payload → `ErrorOr<Success>` |
| `IQuery<ProductDto>` | Read that returns a payload |

**Implication:** handlers implement `IRequestHandler<TRequest, ErrorOr<TResponse>>`. You never write a handler that returns a naked `TResponse` for these contracts — the ErrorOr is part of the request’s type.

---

## 2.7 Using it in a handler — as in MyApp

### Query + handler

```csharp
// Features/Products/GetProduct/GetProductQuery.cs
public sealed record GetProductQuery(Guid Id) : IQuery<ProductDto>, ICacheable
{
    public string CacheKey => $"product:{Id}";
    public TimeSpan? Expiration => TimeSpan.FromMinutes(5);
}

// Features/Products/GetProduct/GetProductQueryHandler.cs
internal sealed class GetProductQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductQuery, ErrorOr<ProductDto>>
{
    public async ValueTask<ErrorOr<ProductDto>> Handle(
        GetProductQuery query, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == query.Id, cancellationToken);

        if (product is null)
        {
            // Expected failure → value, not throw
            return Error.NotFound(
                "PRODUCT.NOT_FOUND",
                $"Product '{query.Id}' was not found.");
        }

        return new ProductDto(
            product.Id, product.Name, product.Category, product.Price, product.Stock);
    }
}
```

Walk through what happens:

1. Mediator resolves the handler (and pipeline).
2. If the row is missing, the handler **returns** an `Error` — the call stack does not unwind via exception.
3. The endpoint will call `MatchOk` (part 3); failure becomes 404 Problem Details.
4. If the row exists, the DTO is returned as success; endpoint writes 200 JSON.

### Create command (returns an id)

```csharp
public sealed record CreateProductCommand(...) : ICommand<Guid>;

// Handler returns ErrorOr<Guid>
// Endpoint: result.MatchCreated(http, id => $"/products/{id}");
```

### Update / delete (void success)

```csharp
public sealed record DeleteProductCommand(Guid Id) : ICommand;

// Handler returns ErrorOr<Success> — Success on delete, Error.NotFound if missing
// Endpoint: result.MatchNoContent(http);  // 204 or problem
```

### What you must *not* do in handlers

| Anti-pattern | Why |
|---|---|
| `throw new NotFoundException()` for missing rows | Moves expected failure to the exception channel; alerts and logs lie |
| Return `null` for not found | No error code, no Problem Details path |
| Catch everything and return `Error.Unexpected` | Hides bugs; prefer let unexpected exceptions bubble to part 5 |

---

## Checkpoint

- You can sketch `ErrorOr<T>`’s success vs failure representation.
- You know why `Match` is safer than `.Value`.
- You know why `ICommand` / `IQuery` wrap `IRequest<ErrorOr<…>>`.
- You can write a handler that returns `Error.NotFound` instead of throwing.

**Next:** [part3.md](part3.md) — the only place that turns `ErrorOr<T>` into HTTP bytes.
