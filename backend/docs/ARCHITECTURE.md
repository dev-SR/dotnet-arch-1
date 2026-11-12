# Architecture Kit — Design, Rules, and Usage

This file is the **single, binding guide** for the backend kit. A new solution built on this kit must be understandable from this file alone. When code and this file disagree, the code is wrong or this file changes in the same change.

Use it when:

- **bootstrapping** a new solution from the kit (§4)
- **adding a feature, module, or service** inside a solution built on the kit (§19)
- **guiding an AI agent or reviewer** — read §1, §3, §13 and the checklist (§17) first

Demo, test, and production code follow the **same rules**. There is no "quick inline exception."

**Naming.** `YourApp` is the product prefix. `Modules.ModuleA` / `ModuleB` are in-process bounded contexts. `Services/YourApp.ServiceA` is an extracted deployable. `Resource` / `CreateResource` are example names. Snippets use these placeholders. §18 lists the known open deviations in the reference solution.

---

## 1. Principles

| Principle | Meaning |
| --- | --- |
| Vertical slice | One use case = one folder: request → handler → (optional) validator → endpoint. |
| Result, not exception | Expected failures are `ErrorOr<T>` values. Exceptions are reserved for bugs and infrastructure faults. |
| Thin handlers | Aggregates own invariants and raise domain events. Handlers orchestrate. |
| Transport-free application layer | Commands and queries hold plain values. HTTP types never cross into the handler. |
| Modular monolith first | One process (`ApiHost`) hosts `YourApp.Modules.*`, each with its own DbContext. |
| Extract when needed | A bounded context moves to `Services/YourApp.ServiceA` with the same slice shape. Services talk through **integration events**. |
| Shared kernel is small | `src/Shared/` holds reusable primitives only. No feature types. |

---

## 2. Technology baseline

| Concern | Choice |
| --- | --- |
| Runtime | .NET 10, C# preview language version, nullable enabled, implicit usings enabled |
| Web | ASP.NET Core minimal APIs with Carter modules (`ICarterModule`) |
| In-process dispatch | Own mediator in `Shared/Mediator` (namespace `Mediator`), not the MediatR NuGet |
| Validation | FluentValidation |
| Persistence | EF Core 10, SQLite per module/service (swap provider in `Infrastructure/`) |
| Messaging | Wolverine with a transactional outbox and inbox, RabbitMQ transport |
| Auth | JWT bearer (HS256), claim-based permissions, optional DB checks in the Users module |
| Gateway | YARP reverse proxy + Scalar, federating each backend's OpenAPI document |
| Caching | `HybridCache` (only where a query opts in with `ICacheable`) |
| Central package versions | `Directory.Packages.props` |

---

## 3. Solution layout

```text
backend/
  Directory.Build.props              TFM, nullable, analyzers, strictness
  Directory.Packages.props           central package versions
  MyApp.slnx                         solution (rename with the project)
  src/
    Shared/                          kit — copy as-is (§5)
      Mediator/
      Domain/
      Common/
      Application/
      YourApp.Modular/
      YourApp.Auth.Jwt/
      YourApp.Messaging/
    Modules/                         in-process bounded contexts
      Directory.Build.props          baseline refs + global usings for every module
      ModuleA/
      ModuleB/
    Services/                        deployables
      YourApp.ApiHost/               composes Modules (one process)
      YourApp.Gateway/               YARP + Scalar
      YourApp.ServiceA/              extracted bounded context (optional)
  tests/
    YourApp.Tests.Unit/
    YourApp.Tests.Integration/
  docs/
    ARCHITECTURE.md                  this file
```

**Modules** are libraries referenced by `ApiHost`. They are not hosts. **Services** are executables with their own `Program.cs`. Both use the same slice, DDD, and result rules.

### 3.1 Project-name conventions

- Namespace and assembly root: `YourApp.*` (e.g. `YourApp.Modules.ModuleA`, `YourApp.Messaging`, `YourApp.Auth.Jwt`).
- Module projects set `AssemblyName` and `RootNamespace` explicitly. Discovery depends on the prefix `YourApp.Modules.` (§19.2).

---

## 4. Bootstrapping a new project

1. Copy `backend/` skeleton: `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `src/Shared/`, `src/Modules/Directory.Build.props`, and one host (`ApiHost`) plus `Gateway` if needed.
2. Rename the product prefix everywhere, in this order:
   - folder names (`MyApp.*` → `YourApp.*`)
   - `.csproj` file names and `AssemblyName` / `RootNamespace`
   - namespaces (search for the old prefix, replace)
   - `MyApp.slnx` project paths
   - `ModuleAssemblyCatalog.ModuleAssemblyPrefix` (must equal `YourApp.Modules.`)
   - `Permissions.Prefix` in `Shared/YourApp.Auth.Jwt` and any titles in OpenAPI transformers
3. Delete sample modules and sample consumers. Keep `Modules/Directory.Build.props`.
4. Create `ModuleA` with the module skeleton (§19.2).
5. Create `ApiHost/Program.cs` as in §12.2.
6. Configure settings (§15): `Jwt`, `ConnectionStrings`, `Cors`, `RateLimit`, `Seed`, `ReverseProxy`.
7. Verify (§16): `dotnet build` with zero errors, `dotnet test` on unit tests, then run the host and check `/health` and `/openapi/v1.json`.

---

## 5. Shared kit (`src/Shared/`)

Copy the whole `Shared/` tree. Feature code never lands here.

| Project | Contents | Use it for |
| --- | --- | --- |
| `Mediator` | `IMediator`, `IRequest<T>`, `IRequestHandler<,>`, `INotification`, `INotificationHandler<>`, `IPublisher`, `IPipelineBehavior<,>`, `RegisterRequestHandlers`, `AddPipelineBehavior` | Sending requests, publishing notifications, pipeline behaviors |
| `Domain` | `Entity`, `Aggregate`, `IAggregate`, `DomainEvent`, `IDomainEvent`, `IEntity` | DDD building blocks |
| `Common` | `ErrorOr<T>`, `Error`, `ErrorType`, `Success`; `ToProblem`, `MatchOk/MatchCreated/MatchNoContent`; `SpecificationBase`, `Apply`, `And/Or`; `PagedResponse`, `ToPagedAsync`; `ExceptionMapping`, `ProblemDetailsExceptionHandler` | Results, HTTP mapping, specs, pagination, exception channel |
| `Application` | `ICommand`, `ICommand<T>`, `IQuery<T>`, `IPersistOnFailure`, `ICacheable`; `LoggingBehavior`, `ValidationBehavior`, `CachingBehavior`, `TransactionBehavior`; `AddTransactionalDbContext`, `IDbContextCommitFlush` | Pipeline and persistence hooks |
| `YourApp.Modular` | `ModuleAssemblyCatalog`, `AddCarterWithAssemblies`, `AddModularEfCore`, `IUserContext`, `DbContextSchema.Apply`, `IDataSeeder`, `UseDevelopmentSeeders` | Module discovery, EF audit, schema bootstrap, seeding |
| `YourApp.Auth.Jwt` | `AddMyAppAuth` (rename to `Add{YourApp}Auth`), `AddMyAppAuthorization`, `PermissionRequirement`, `ResourcePermissionRequirement`, `IOwnedResource`, `Caller`, `ResourceAccess`, `Permissions`, `Roles`, `AuthClaimTypes`, `JwtOptions`, `JwtBearerProblemDetails` | Token validation, the caller as plain values, permission and ownership rules |
| `YourApp.Messaging` | `IIntegrationEvent`, `IntegrationEvent`, integration contracts, `MessagingQueues`, `TransactionalMessagingExtensions`, `TransactionalDomainEventInterceptor`, `WolverineDbContextCommitFlush` | Contracts, outbox, broker wiring |

Shared projects reference only other Shared projects and framework packages. Feature code never references `Shared` internals beyond these public types.

---

## 6. Vertical slice — the HTTP use case

Every HTTP use case (in a module or a service) has this shape:

### 6.1 Folder layout

**Producer use case** (writes an aggregate and may raise events):

```text
{Area}/{UseCase}/
  {UseCase}Command.cs | {UseCase}Query.cs
  {UseCase}CommandHandler.cs | {UseCase}QueryHandler.cs
  {UseCase}Endpoint.cs              ← ICarterModule only
  {UseCase}Validator.cs
  Events/
    {Fact}DomainEvent.cs            ← raised by the aggregate for this use case
    Publish{Fact}IntegrationHandler.cs   ← domain → integration bridge
    Log{Fact}Handler.cs             ← optional in-process reaction
```

If one domain event is raised from more than one use case, move it to `{Area}/Events/` and do not duplicate it.

**Consumer** (inbound integration event, no HTTP):

```text
Consumers/{Reaction}/               ← named after what THIS context does
  {IntegrationEvent}Handler.cs      ← Wolverine handler; must be idempotent
```

Group handlers for one capability in one folder (e.g. `ProductCreated` and `ProductUpdated` → `Consumers/IndexProduct/`).

**Area-level types** shared by use cases of the same aggregate:

```text
{Area}/
  {Aggregate}.cs
  Specifications/
```

### 6.2 Command and query

```csharp
public sealed record CreateResourceCommand(string Name, decimal Amount) : ICommand<Guid>;
public sealed record UpdateResourceCommand(Guid Id, string Name) : ICommand;   // 204 on success
public sealed record GetResourceQuery(Guid Id) : IQuery<ResourceDto>;
```

| Marker | Pipeline effect |
| --- | --- |
| `ICommand` / `ICommand<T>` | `ICommandMarker` → `TransactionBehavior` saves and commits. Returns `ErrorOr<Success>` / `ErrorOr<T>`. |
| `IQuery<T>` | No write path. Returns `ErrorOr<T>`. |
| `IPersistOnFailure` (opt-in) | Commits even when the handler returns an error. Use only for deliberate state changes on failure (e.g. failed-login counters, refresh-token family revocation). |
| `ICacheable` (opt-in, queries) | `CacheKey` and optional `Expiration`. Successes are cached. Failures are never cached. |

**Rules for command and query parameters:**

- Use primitives, `Guid`, strings, numbers, and small records from the application layer.
- Do **not** put `HttpContext`, `ClaimsPrincipal`, request DTOs from the transport, or EF entities in a command or query.
- Who is calling goes in as a `Caller` (§13.4), built once by the endpoint from the principal with `Caller.From(user)`.
- Plain ids (`Guid`) for the target resource stay as plain parameters.

### 6.3 Validator

```csharp
public sealed class CreateResourceValidator : AbstractValidator<CreateResourceCommand>
{
    public CreateResourceValidator()
    {
        RuleFor(x => x.Name).MinimumLength(3).MaximumLength(100);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
```

Input shape only. Domain invariants stay in the aggregate and return `ErrorOr` from factories and mutators. FluentValidation errors become `Error.Validation` with `Code` = property name (§7.2).

### 6.4 Endpoint — transport only

```csharp
public sealed class CreateResourceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/resources", HandleAsync)
            .WithName("CreateResource")
            .WithTags("Resources")
            .RequireAuthorization(Permissions.Resources.Create);
    }

    private static async Task<IResult> HandleAsync(
        CreateResourceCommand command,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return result.MatchCreated(http, id => $"/resources/{id}");
    }
}
```

| Outcome | Helper |
| --- | --- |
| 200 + body | `MatchOk(http)` |
| 201 + `Location` | `MatchCreated(http, id => $"/resources/{id}")` |
| 204 | `MatchNoContent(http)` (command returns `ErrorOr<Success>`) |
| Failure | Mapped by the helpers through `ToProblem` (§7.4) |

**Forbidden in endpoints:** `DbContext`, `SaveChanges`, business rules, outbox or broker clients, `Results.*` for domain outcomes, passing `ClaimsPrincipal` into a command or query (build a `Caller` instead).

When the body is not the command, map first:

```csharp
var result = await mediator.Send(
    new AddItemCommand(user.GetUserId(), body.ProductId, body.Quantity), ct);
return result.MatchNoContent(http);
```

Anonymous endpoints use `.AllowAnonymous()` explicitly. Endpoints are otherwise covered by the host fallback policy (§12.4).

### 6.5 Handler — orchestration

```csharp
internal sealed class CreateResourceCommandHandler(ModuleADbContext db)
    : IRequestHandler<CreateResourceCommand, ErrorOr<Guid>>
{
    public ValueTask<ErrorOr<Guid>> Handle(
        CreateResourceCommand command,
        CancellationToken cancellationToken)
    {
        var created = Resource.Create(command.Name, command.Amount);
        if (created.IsError)
            return ValueTask.FromResult<ErrorOr<Guid>>(created.Errors.ToList());

        db.Resources.Add(created.Value);
        // NO SaveChanges — TransactionBehavior saves and commits.
        // NO outbox.PublishAsync — the domain event bridge publishes the integration event.
        return ValueTask.FromResult<ErrorOr<Guid>>(created.Value.Id);
    }
}
```

Rules:

- Return the **id or a DTO**, never the EF entity.
- Use `ValueTask` for `Handle` and `ValueTask.FromResult` when there is no await.
- Ownership and permission checks on a loaded resource run in the handler, using `ResourceAccess` (§13.3). A failed check returns `Error.Forbidden`.
- Queries project to DTOs (§9). They do not return entities.

---

## 7. Results, errors, and exceptions

### 7.1 Two channels

| Channel | Used for | Produced by | HTTP result |
| --- | --- | --- | --- |
| **Result** | Expected outcomes: validation, not found, conflict, forbidden, unauthorized, dependency down | Handlers return `ErrorOr<T>` | `Match*` → `ToProblem` |
| **Exception** | Bugs and infrastructure faults | Anything thrown | `ProblemDetailsExceptionHandler` |

A handler **must not** throw for an expected outcome. A throw in a handler is a bug, logged and returned as 500.

### 7.2 `Error` and `ErrorType`

```csharp
Error.NotFound("ORDER.NOT_FOUND", "Order was not found.");
Error.Conflict("STOCK.INSUFFICIENT", "Not enough stock.");
Error.Forbidden("AUTH.FORBIDDEN", "Not allowed.");
Error.Unauthorized("AUTH.INVALID_CREDENTIALS", "Invalid credentials.");
Error.Validation("PRODUCT.PRICE", "Price must be greater than zero.");
Error.ValidationField("Email", "Email is invalid.");        // Code = field name
Error.Failure("X.BAD_INPUT", "Malformed input.");           // 400, not a field rule
Error.ServiceUnavailable("Payment provider is down.");       // 503, default code SERVICE_UNAVAILABLE
Error.Unexpected("X.UNEXPECTED", "Internal detail");        // 500; description is never sent to clients
```

- `Code` is **stable and machine-readable**. Clients switch on it. Use `AREA.NAME` style.
- `Description` is human-readable. On 5xx it is replaced with `"An unexpected error occurred."`.
- `Error` requires a non-empty `Code` and `Description`. The constructor throws on empty input (a programmer error).

| `ErrorType` | HTTP | Meaning |
| --- | --- | --- |
| `Failure` | 400 | Malformed request, not a field rule |
| `Validation` | 400 | A field failed a rule |
| `Unauthorized` | 401 | No or invalid credentials |
| `Forbidden` | 403 | Authenticated, not allowed |
| `NotFound` | 404 | Missing resource |
| `Conflict` | 409 | Current state disallows the action (includes business rules) |
| `ServiceUnavailable` | 503 | Dependency down; retry later |
| `Unexpected` | 500 | Anything else |

Map status in one place only: `ErrorExtensions.MapStatusCode`.

### 7.3 `ErrorOr<T>` usage

```csharp
ErrorOr<Guid> r = ...;

r.IsError            // check
r.Value              // only when !IsError (throws otherwise)
r.Errors             // list, never null
r.FirstError         // convenience for logging

r.Match(v => ..., errs => ...);            // sync
await r.MatchAsync(v => ..., errs => ...); // async
r.Then(v => NextStep(v));                  // short-circuit composition
```

Build failures with `ErrorOr<T>.From(list)`, or return a single `Error` / `List<Error>` through implicit conversion.

### 7.4 ProblemDetails mapping

All failures are RFC 9457 Problem Details with these extensions:

| Extension | Value |
| --- | --- |
| `traceId` | `HttpContext.TraceIdentifier` (use it in support requests) |
| `errorCode` | `Error.Code` (`VALIDATION` for grouped validation errors) |
| `errorType` | `ErrorType` name |

Rules:

- A **single** failure returns one problem with the status above.
- A **list** of validation errors returns one `ValidationProblemDetails` (400) grouped by field `Code`.
- A list that mixes validation and non-validation errors returns the **first non-validation error** (the caller should not learn field rules before being told they are not allowed).
- An empty error list returns 500 `EMPTY_ERROR_LIST` (never a silent 200).

### 7.5 Exception channel

`AddSharedExceptionHandling()` and `UseSharedExceptionHandling()` register the global handler. `ExceptionMapping.Map`:

| Exception | Status | Code |
| --- | --- | --- |
| `BadHttpRequestException` | its own status | `BAD_REQUEST` |
| `DbUpdateConcurrencyException` | 409 | `CONCURRENCY_CONFLICT` |
| `OperationCanceledException` | 499 | `REQUEST_ABORTED` (no body if the request was aborted) |
| anything else | 500 | `INTERNAL_ERROR` |

`TransactionBehavior` converts a unique-constraint violation (`DbUpdateException` from SQLite) into `Error.Conflict("DB.CONFLICT", …)`. Any other `DbUpdateException` becomes an exception and is handled above.

In Development, 5xx responses include the exception message. Elsewhere they do not.

### 7.6 JWT challenge and forbid

Authentication failures use the same ProblemDetails shape (`JwtBearerProblemDetails`):

| Situation | Status | `errorCode` |
| --- | --- | --- |
| No credentials | 401 | `AUTH.UNAUTHENTICATED` |
| Invalid/expired token | 401 | `AUTH.INVALID_TOKEN` (`WWW-Authenticate` set accordingly) |
| Banned / token revoked (Users host only) | 401 | `AUTH.ACCOUNT_DISABLED` / `AUTH.TOKEN_REVOKED` |
| Authenticated, missing permission | 403 | `AUTH.FORBIDDEN` |

---

## 8. Domain — aggregates, invariants, domain events

### 8.1 Aggregate

```csharp
public sealed class Resource : Aggregate
{
    private Resource() { }   // EF

    public string Name { get; private set; } = null!;
    public decimal Amount { get; private set; }

    public static ErrorOr<Resource> Create(string name, decimal amount)
    {
        var errors = Validate(name, amount);
        if (errors.Count > 0)
            return errors;

        var resource = new Resource
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Amount = amount,
        };
        resource.AddDomainEvent(new ResourceCreatedDomainEvent(resource.Id, resource.Name));
        return resource;
    }

    public ErrorOr<Success> Rename(string name)
    {
        if (Validate(name, Amount) is { Count: > 0 } errors)
            return errors;
        Name = name.Trim();
        return default(Success);
    }
}
```

Rules:

- Private parameterless constructor for EF. Mutate only through domain methods.
- No public setters on aggregate properties. State changes go through factories and domain methods, so the invariants hold on every path.
- Child entities are created by the aggregate (for example through an `internal` constructor), never by handlers.
- Factories and mutators return `ErrorOr`. They never throw for invalid input.
- An aggregate **does not reach into another aggregate's state**. Change one aggregate per command, or coordinate through a domain event.
- Aggregates do not call repositories, services, or the outbox.

### 8.2 Domain event

```csharp
// CreateResource/Events/ResourceCreatedDomainEvent.cs
namespace YourApp.Modules.ModuleA.Resources.CreateResource.Events;

public sealed record ResourceCreatedDomainEvent(Guid ResourceId, string Name) : DomainEvent;
```

| Rule | Detail |
| --- | --- |
| Who raises | The aggregate, through `AddDomainEvent`. |
| Where the type lives | `{UseCase}/Events/` for the use case that caused the fact. Move to `{Area}/Events/` only when shared. |
| In-process reaction | `INotificationHandler<TEvent>` in the same `Events/` folder. No cross-context I/O. |
| Domain → integration | `Publish…IntegrationHandler` in the same `Events/` folder, calling `IDbContextOutbox.PublishAsync`. |
| Dispatch | `TransactionalDomainEventInterceptor` on `SavingChangesAsync`. No event-type `switch` anywhere. |

Handlers run inside `SaveChanges` and **must not call `SaveChanges`**. Use `SaveChangesAsync` whenever domain events may be pending. The synchronous `SaveChanges` throws if events are pending.

Events raised during dispatch are not published in the same save. They stay on the aggregate until a later save.

---

## 9. Specifications and pagination

### 9.1 Specification

```csharp
public sealed class ActiveResourceSpec : SpecificationBase<Resource>
{
    public ActiveResourceSpec() => Where(r => r.IsActive && !r.IsDeleted);

    public static readonly ISpecification<Resource> Instance = new ActiveResourceSpec();
}

public sealed class AmountRangeSpec(decimal? min, decimal? max) : SpecificationBase<Resource>
{
    // Compose only the parts that apply.
}
```

### 9.2 Query handler

```csharp
ISpecification<Resource> spec = ActiveResourceSpec.Instance;
if (filter.MinAmount is not null || filter.MaxAmount is not null)
    spec = spec.And(new AmountRangeSpec(filter.MinAmount, filter.MaxAmount));

var projected = db.Resources
    .Apply(spec)
    .Select(r => new ResourceListItemDto { Id = r.Id, Name = r.Name, Amount = r.Amount });

var page = await projected.ToPagedAsync(filter, cancellationToken);
return new PagedResponse<ResourceListItemDto>(page.Items, page.TotalCount, page.Page, page.PageSize);
```

Specs and projections go in the module's `Specifications/` folder. Handlers do not build `Where` lambdas inline for reusable filters.

---

## 10. Pipeline and persistence

### 10.1 Registration (in `Application/DependencyInjection.cs`)

```csharp
services.RegisterRequestHandlers(assemblies);
services.AddHybridCache();
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
services.AddPipelineBehavior(typeof(CachingBehavior<,>));
services.AddPipelineBehavior(typeof(TransactionBehavior<,>));
services.AddValidatorsFromAssemblies(assemblies, includeInternalTypes: true);
```

Order: outermost first. Logging wraps everything. Validation runs before any work. Caching runs before the transaction, so a cache hit never opens a transaction. `TransactionBehavior` is innermost.

**ApiHost** registers all four behaviors. **Extracted services** register Validation and Transaction, and may add Logging and Caching when a query needs it.

Assemblies come from `ModuleAssemblyCatalog.GetHostAndModuleAssemblies(hostAssembly)` in ApiHost, and from the service's own assembly in services.

### 10.2 `CachingBehavior` (what it does)

- Runs only for requests that implement `ICacheable`.
- Works with `ErrorOr<T>` responses: it caches the unwrapped `T` on success and never caches failures.
- `CacheKey` must include every input that changes the result (ids, filters, paging, caller scope). Otherwise callers see each other's data.
- `Expiration` is optional. Set it for data that can go stale.

### 10.3 `TransactionBehavior` (what it does)

For `ICommandMarker` requests only:

1. Run the handler.
2. If the response is `ErrorOr` failure and the request is not `IPersistOnFailure`, clear change tracking and return the error.
3. Otherwise save changed contexts, commit, then call each `IDbContextCommitFlush` (outbox flush).
4. A unique-constraint `DbUpdateException` becomes `Error.Conflict`.

Queries do not open a transaction. Do not call `SaveChanges` in a handler.

### 10.4 DbContext registration (in `Infrastructure/DependencyInjection.cs`)

```csharp
services.AddTransactionalDbContext<ModuleADbContext>((sp, options) =>
{
    options.UseSqlite(connectionString);
    options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
});
```

`AddTransactionalDbContext` also registers the context as `DbContext`, which `TransactionBehavior` uses. Always attach interceptors from DI (audit and domain-event dispatch depend on it).

---

## 11. Messaging — domain → integration → consume

### 11.1 Roles

| Role | Owner | Does |
| --- | --- | --- |
| Raise domain event | Aggregate | `AddDomainEvent` |
| In-process reaction | `INotificationHandler<TDomainEvent>` in the producing use case | Logging, same-context side effects |
| Domain → integration | `INotificationHandler<TDomainEvent>` (`Publish…IntegrationHandler`) | `IDbContextOutbox.PublishAsync` |
| Trigger | `TransactionalDomainEventInterceptor` | Enrolls the outbox, publishes a snapshot of pending events, clears them |
| Commit and flush | `TransactionBehavior` + `WolverineDbContextCommitFlush` | Save → commit → flush to the broker |
| Consume | Wolverine handler in `Consumers/{Reaction}/` | Idempotent projection or side effect |
| Wire transport | Module `ConfigureMessaging` / service `Use…Messaging` | Stores, exchanges, queues, handler discovery |

Integration contracts live in `YourApp.Messaging/IntegrationEvents/`. A consumer never references another context's domain types.

### 11.2 Integration event

```csharp
// YourApp.Messaging/IntegrationEvents/ResourceCreatedIntegrationEvent.cs
public sealed record ResourceCreatedIntegrationEvent(Guid ResourceId, string Name) : IntegrationEvent;
```

### 11.3 Publisher bridge

```csharp
// CreateResource/Events/PublishResourceCreatedIntegrationHandler.cs
public sealed class PublishResourceCreatedIntegrationHandler(IDbContextOutbox outbox)
    : INotificationHandler<ResourceCreatedDomainEvent>
{
    public async ValueTask Handle(ResourceCreatedDomainEvent notification, CancellationToken cancellationToken)
    {
        await outbox.PublishAsync(
            new ResourceCreatedIntegrationEvent(notification.ResourceId, notification.Name));
    }
}
```

### 11.4 Consumer

```csharp
// Consumers/IndexResource/ResourceCreatedIntegrationEventHandler.cs
public sealed class ResourceCreatedIntegrationEventHandler
{
    public async Task Handle(
        ResourceCreatedIntegrationEvent message,
        ServiceADbContext db,
        CancellationToken ct)
    {
        if (await db.Projections.AnyAsync(x => x.ResourceId == message.ResourceId, ct))
            return; // idempotent

        db.Projections.Add(new ResourceProjection { ResourceId = message.ResourceId, Name = message.Name });
        await db.SaveChangesAsync(ct);
    }
}
```

Consumers are not mediator requests, so they save directly. They must be idempotent: redelivery is normal.

### 11.5 Module transport configuration

```csharp
// ModuleA/ModuleADependencyInjection.cs (facade)
public static void ConfigureMessaging(TransactionalMessagingOptions msg, IConfiguration config)
{
    msg.AddStore<ModuleADbContext>(
        TransactionalMessagingOptions.RequireConnectionString(config, "ModuleA"),
        MessageStoreRole.Main);
    msg.PublishToExchange<ResourceCreatedIntegrationEvent>(MessagingQueues.ResourceEventsExchange);
}
```

A service listens with `msg.ListenTo(queue, bindToFanoutExchange)` and registers handlers with `msg.IncludeHandler<THandler>()`. Discovery is conventional-off: only registered handlers run.

Exchanges and queues are constants in `MessagingQueues`. Do not hard-code broker names in features.

---

## 12. Hosts — layers and composition

Every host (`ApiHost` and each extracted service) has the same three layer folders. `Program.cs` only chains `Add*` / `Use*` / `MapCarter`.

### 12.1 Layer responsibilities

| Folder | Extension(s) | Owns |
| --- | --- | --- |
| `Application/` | `AddApplication()` | Handler discovery, validators, pipeline behaviors |
| `Infrastructure/` | `AddInfrastructure(config)`, `Use…Messaging()`, `UseInfrastructure()` | Persistence registration, messaging wiring, schema bootstrap on startup |
| `Presentation/` | `AddApi(config)`, `UseSharedPipeline()` | Auth, Carter scan, OpenAPI, exception middleware, health, CORS, rate limit |

**Not allowed at the project root:** `*MessagingExtensions.cs`, DI registration in `Program.cs`, `MapGet`/`MapPut` feature handlers, options registration outside the layer that uses it.

### 12.2 ApiHost `Program.cs` (target shape)

```csharp
using DotNetEnv;
using YourApp.Application;
using YourApp.Infrastructure;
using YourApp.Modular;
using YourApp.Modules.ModuleA;
using YourApp.Modules.ModuleB;
using YourApp.Presentation;

Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddModularEfCore()
    .AddApi(builder.Configuration)
    .AddModuleA(builder.Configuration)
    .AddModuleB(builder.Configuration);

builder.UseApiHostMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.UseModuleA();
app.UseModuleB();
app.UseDevelopmentSeeders();
app.MapCarter();

app.Run();

public partial class Program;
```

- `UseApiHostMessaging()` calls each module's `ConfigureMessaging` (§11.5).
- `AddModularEfCore()` belongs to ApiHost only. It registers the audit interceptor and `IUserContext`.
- `UseDevelopmentSeeders()` runs after schema setup and only in Development.

### 12.3 Extracted service — same layers

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

builder.UseServiceAMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.MapCarter();

app.Run();
```

Service layers:

```text
Services/YourApp.ServiceA/
  Program.cs
  Application/DependencyInjection.cs         → AddApplication()
  Infrastructure/DependencyInjection.cs      → AddInfrastructure(config)   (DbContext + interceptors)
  Infrastructure/MessagingExtensions.cs      → UseServiceAMessaging()
  Infrastructure/WebApplicationExtensions.cs → UseInfrastructure()        (DbContextSchema.Apply)
  Presentation/DependencyInjection.cs        → AddApi(config)             (auth, Carter, OpenAPI, exceptions, health)
  Presentation/WebApplicationExtensions.cs   → UseSharedPipeline()        (exception handling, auth, OpenAPI, /health)
  Presentation/OpenApiAuthExtensions.cs
  {Area}/{UseCase}/                          vertical slices (§6)
  Consumers/{Reaction}/                      inbound integration handlers (§11.4)
  AssemblyMarker.cs
```

### 12.4 Shared presentation pipeline

`AddApi` registers, in order: shared exception handling and ProblemDetails, auth (`AddMyAppAuth`), host fallback policy (`RequireAuthenticatedUser`), Carter scan, OpenAPI with a Bearer scheme and per-operation security (anonymous operations excluded), health checks, and (ApiHost only) CORS and rate limiting.

`UseSharedPipeline` adds exception handling, forwarded headers, CORS, rate limiting, authentication, authorization, `MapHealthChecks("/health").AllowAnonymous()`, and `MapOpenApi().AllowAnonymous()`.

### 12.5 Module tree

```text
Modules/ModuleA/
  DependencyInjection.cs         → AddModuleA(config)     DbContext, seeders, options, policies
  ModuleAModule.cs               → UseModuleA(), ConfigureMessaging(...)
  Persistence/                   ModuleADbContext, configurations, seeding
  {Area}/{UseCase}/              vertical slices
  Consumers/{Reaction}/          optional inbound handlers
```

A module is **not a host**. It does not own `Program.cs`, a Wolverine host, or the HTTP pipeline. `UseModuleA` runs schema bootstrap through `DbContextSchema.Apply`.

---

## 13. Authentication and authorization

### 13.1 Token

- HS256 JWT. `Jwt:Secret` must be at least 32 UTF-8 bytes (256 bits); startup fails otherwise.
- `Jwt:Issuer`, `Jwt:Audience`, `Jwt:AccessTokenMinutes` (default 10), `Jwt:RefreshTokenDays` (default 7).
- Validation: issuer, audience, lifetime (30 s clock skew), signing key, algorithm `HS256`. `MapInboundClaims = false`.
- Claims used: `sub` (user id, a GUID), `role`, `permission`, `token_version`, `amr`.

Validation for every host is in `AddMyAppAuth`. The Users host adds `ConfigureJwtBearer` (ban and token-version checks against the database). Other hosts trust claims and do not query the database.

### 13.2 Permissions

Permission strings follow `Permissions.<Area>.<Action>`, for example `Permissions.Products.Create`. Define them in one static class in `YourApp.Auth.Jwt` (`Permissions`) and list every one in `Permissions.All`.

```csharp
.RequireAuthorization(Permissions.Products.Create)
```

The policy provider turns any name starting with `Permissions.` into a requirement that succeeds when the caller has a `permission` claim with that exact value (case-insensitive). Policies are resolved by name. There is no DB permission lookup in the hot path.

Roles (`Roles.User`, `Roles.Admin`) are coarse. Put fine-grained rules in permissions.

### 13.3 Ownership and resource checks

A handler loads the resource, then calls `ResourceAccess.IsAllowed` with the caller's `Caller` value:

```csharp
if (!ResourceAccess.IsAllowed(command.Caller, invoice.OwnerId, Permissions.Invoices.Edit))
    return Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to edit this invoice.");
```

The rule:

- The caller needs the permission (case-insensitive match against the `permission` claims).
- With ownership required (the default), the caller also needs `sub == OwnerId` or the `Admin` role.
- `requireOwnership: false` checks the permission only, for example a "view any" permission.
- An unauthenticated caller (empty id) never matches an owner.

`ResourceAccess` is the only implementation of this rule. The policy handler `ResourcePermissionHandler` delegates to it for endpoints that use `IAuthorizationService`, so the two cannot disagree.

### 13.4 Passing the caller

- The endpoint builds the caller once: `var caller = Caller.From(user);`.
- `Caller` is a record of plain values: `UserId`, `IsAdmin`, `Permissions`. It is the only identity type that crosses into commands and queries.
- Commands and queries that need an ownership or permission check take `Caller Caller` as a parameter.
- `ClaimsPrincipal` and `HttpContext` never appear in a command, query, or handler signature. The architecture tests enforce this.

### 13.5 Users module (login, refresh, admin)

- `POST /auth/login` (`ICommand<TokenResponse>`, `IPersistOnFailure`): verifies the password with `IPasswordHasher`, locks the account for 15 minutes after 5 failures, burns a dummy verify on unknown users to resist timing probes.
- `POST /auth/refresh` (`ICommand<TokenResponse>`, `IPersistOnFailure`): rotates the refresh token. A reused token revokes its whole family.
- `POST /auth/logout`, `POST /auth/logout-all`, `POST /auth/token` (OAuth2 password-form adapter for Scalar).
- Auth endpoints use `AllowAnonymous()` and the `auth` rate-limit policy (`RateLimit:AuthPerMinute`).
- Admin endpoints require `Roles.Admin` or specific permissions.

### 13.6 Hosts without the Users module

Hosts that only validate tokens (services) use `AddMyAppAuth` with claim-only checks. A token that is valid but revoked is accepted until it expires (`AccessTokenMinutes`). Keep access tokens short.

---

## 14. Gateway

- `Services/YourApp.Gateway` uses YARP (`ReverseProxy` section) to route public paths to backend clusters.
- It **does not validate JWTs**. It forwards `Authorization`. Each backend validates.
- Scalar shows one document per backend. The gateway fetches each backend's `/openapi/v1.json` and rewrites `servers` to the gateway's public URL.
- A cluster name describes the **backend it targets**. Name it after the host (e.g. `apihost`, `invoices`), not after a feature.

---

## 15. Configuration

| Key | Used by | Notes |
| --- | --- | --- |
| `ConnectionStrings:<Module or Service>` | each DbContext, messaging stores | Falls back to a local SQLite file only in development |
| `ConnectionStrings:RabbitMq` (or `Messaging:RabbitMq`) | messaging | Required. Startup fails without it. |
| `Jwt:*` | `AddMyAppAuth` | §13.1 |
| `Cors:AllowedOrigins` | ApiHost | Production policy uses this list. Development allows any origin. |
| `RateLimit:GlobalPerMinute` (default 120) and `RateLimit:AuthPerMinute` (default 5) | ApiHost | Validated at startup. |
| `Seed:*` | Users seeder | Development seeding only. |
| `ReverseProxy:*`, `OpenApi:*` | Gateway | Routes, clusters, backend URLs for OpenAPI proxy |

Load local values from `.env` through `DotNetEnv` in ApiHost only. Never commit secrets. Use user secrets or environment variables for `Jwt:Secret` and connection strings in deployed environments.

---

## 16. Testing and verification

| Layer | What to test | Tools |
| --- | --- | --- |
| Domain | Aggregate invariants, factory errors, event raised | xUnit, FluentAssertions |
| Pipeline | Validation short-circuit, caching (success cached, failure not), transaction commit/rollback | xUnit with `NullLogger`, in-memory or SQLite |
| Mapping | `ErrorType` → status, ProblemDetails shape, list grouping | xUnit |
| Endpoint and integration | HTTP contract, auth, ProblemDetails over the wire; messaging round trips | `WebApplicationFactory`, RabbitMQ and other infrastructure through docker compose |
| Architecture | Every "must" in this file that code can check (layering, slice shape, handler return types, aggregates, event placement, layout, forbidden calls) | `tests/YourApp.Tests.Architecture`: NetArchTest.Rules for dependencies, reflection for shape, source scans for layout and calls |

**Architecture tests are the enforcement of this file.** They run with the unit tests. A rule that fails means either the code is wrong or the rule needs a design change in this file. Never weaken a rule to make a build pass. The few accepted exceptions are listed explicitly inside the architecture tests, and each one maps to a row in §18. A new exception needs a new §18 row first.

Commands:

```text
dotnet build YourApp.slnx
dotnet test tests/YourApp.Tests.Unit
dotnet test tests/YourApp.Tests.Architecture
dotnet test tests/YourApp.Tests.Integration     # needs docker compose (RabbitMQ, Elasticsearch)
```

Integration tests expect RabbitMQ at `localhost:5672` and Elasticsearch at `localhost:9200`. If the infrastructure is missing, the tests fail on connection, not on code. Treat them as **not run** in that case.

Demo and test code obey the same rules. Tests do not mock `ErrorOr` away. They assert on `IsError`, `Errors`, and the HTTP status.

---

## 17. Non-negotiables (review checklist)

- [ ] HTTP surface is `ICarterModule` in a use-case folder.
- [ ] Endpoint only calls `mediator.Send` and `Match*` / `ToProblem`. No `DbContext`, no `Results.*` for domain outcomes.
- [ ] Commands and queries carry primitives, records, or a `Caller`. Never `ClaimsPrincipal`, `HttpContext`, or EF entities.
- [ ] Handlers return `ErrorOr<T>` or `ErrorOr<Success>`; expected failures are results, not exceptions.
- [ ] Writes are `ICommand` / `ICommand<T>`. No `SaveChanges` in endpoints or handlers.
- [ ] Aggregates raise domain events. Event types and their handlers sit under `{UseCase}/Events/`.
- [ ] Integration events are published only from `INotificationHandler` bridges.
- [ ] Consumers live under `Consumers/{Reaction}/`, are idempotent, and reference only shared contracts.
- [ ] No `switch (domainEvent)` anywhere; dispatch is generic.
- [ ] Every host has `Application/`, `Infrastructure/`, `Presentation/`. `Program.cs` only chains `Add*` / `Use*` / `MapCarter`.
- [ ] No root-level `*MessagingExtensions.cs`. Messaging lives in `Infrastructure/` or the module facade.
- [ ] Schema bootstrap uses `DbContextSchema.Apply`. Health uses `MapHealthChecks`.
- [ ] Cross-context data flows only through integration events or explicit contracts. Never shared EF entities.
- [ ] Permissions come from one `Permissions` class. Policies use the `Permissions.` prefix.
- [ ] `CacheKey` includes every input that changes the result.
- [ ] Aggregates have no public setters and expose factories and domain methods only.
- [ ] Architecture tests pass. Any new exception has a §18 row.
- [ ] Unit tests pass; integration tests run or are explicitly reported as not run.

---

## 18. Known deviations in the reference solution

These are open gaps in the current code, not part of the design. Each one must be fixed or accepted explicitly before a new project copies the code. Resolved items are removed from this table.

| ID | Deviation | Rule it breaks | Status |
| --- | --- | --- | --- |
| D1 | Extracted services register Validation and Transaction behaviors but not `LoggingBehavior` or `CachingBehavior` | §10.1 | Accepted. Add Logging when a service needs request timing. |
| D2 | `UseDevelopmentSeeders` blocks on async seeding with `GetAwaiter().GetResult()` | Async rule | Open. Development only. Make it async. |
| D3 | `CreateProductCommandHandler` creates a new `Category` and `Brand` on every request | Aggregate design | Open. Look up or create by name. |
| D4 | `Error.ServiceUnavailable(description, code)` takes arguments in a different order from the other factories | Consistent API | Open. Breaking change for callers; do it with the next major kit version. |
| D5 | `Features/Diagnostics` (ApiHost) and the auth policy demo return `Results.Ok` directly | §6.4 | Accepted. Diagnostics run only in Development and Testing. The policy demo exists to exercise the policies. |
| D6 | `UseSearchModule` blocks on the Elasticsearch index bootstrap at startup (`GetAwaiter().GetResult()`) | Async rule | Accepted for startup only. Move to an async startup hook when the host supports one. |

## 19. Recipes

### 19.1 New feature in an existing module or service

1. Create `{Area}/{UseCase}/` with command or query, handler, endpoint, validator (§6).
2. If the aggregate's state changes, extend the aggregate through a domain method, add `{Fact}DomainEvent`, and add the in-process and integration bridges under `{UseCase}/Events/` (§8, §11).
3. In each consuming context, add `Consumers/{Reaction}/` for the integration event (§11.4).
4. For list endpoints, add specifications under `{Area}/Specifications/` and use `ToPagedAsync` (§9).
5. Do not change `Program.cs` unless you add a module to ApiHost.
6. Add unit tests for the aggregate and the handler's error branches. Add integration tests for HTTP and messaging.

### 19.2 New module `YourApp.Modules.ModuleC`

1. Create `src/Modules/ModuleC/ModuleC.csproj` (inherits `Modules/Directory.Build.props`). Set `AssemblyName` to `YourApp.Modules.ModuleC`.
2. Add `DependencyInjection.cs` (`AddModuleC`) and `ModuleCModule.cs` (`UseModuleC`, optional `ConfigureMessaging`).
3. Add `ModuleCDbContext` with `AddTransactionalDbContext` and interceptors (§10.4).
4. Reference the module from ApiHost. Call `AddModuleC` and `UseModuleC` in `Program.cs`. Call `ConfigureMessaging` from `Infrastructure/ApiHostMessagingExtensions.cs`.
5. Discovery is automatic: the module is found by its `YourApp.Modules.` prefix.

### 19.3 New service `YourApp.ServiceC`

1. Copy an existing service (`Application/`, `Infrastructure/`, `Presentation/`, `Program.cs`). Rename it (§4).
2. Keep `Program.cs` as in §12.3.
3. Add vertical slices only (§6).
4. Add a Gateway route, cluster, and OpenAPI document entry.
5. Exchange data through Messaging contracts, never through another service's DbContext.

---

## 20. Reference paths (current solution)

Use these as examples for the patterns above. Paths are relative to `backend/`.

| Pattern | Path |
| --- | --- |
| Producer slice | `src/Modules/Catalog/Products/CreateProduct/` |
| Producer events | `src/Modules/Catalog/Products/CreateProduct/Events/`, `…/UpdateProduct/Events/` |
| Query, specs, pagination | `src/Modules/Catalog/Products/GetProducts/`, `…/Products/Specifications/` |
| Cacheable query | `src/Modules/Catalog/Products/GetProduct/GetProductQuery.cs` |
| Aggregate | `src/Modules/Catalog/Products/Product.cs` |
| Auth flows | `src/Modules/Users/Auth/Login/`, `…/Refresh/`, `…/Logout/` |
| Consumer (module) | `src/Modules/Search/Consumers/IndexProduct/` |
| Consumer (service) | `src/Services/*Api/Consumers/*/` |
| Caller and ownership rule | `src/Shared/MyApp.Auth.Jwt/Caller.cs`, `ResourceAccess.cs`; used by `src/Services/MyApp.InvoicesApi/GetInvoice/GetInvoiceQueryHandler.cs` |
| Aggregate with a factory | `src/Services/MyApp.OrdersApi/Orders/Order.cs` (`Order.Place`), `OrderItem.cs`, `OrderLine.cs` |
| Module facade and messaging | `src/Modules/Catalog/DependencyInjection.cs`, `CatalogModule.cs` |
| Host composition | `src/Services/MyApp.ApiHost/Program.cs` and its `Application/`, `Infrastructure/`, `Presentation/` |
| Service composition | `src/Services/MyApp.OrdersApi/Program.cs` |
| Shared kit | `src/Shared/` |
| Gateway | `src/Services/MyApp.Gateway/` |
| Unit tests for pipeline and mapping | `tests/MyApp.Tests.Unit/` |

When unsure, copy the **CreateProduct** slice shape, not an inline handler in `Program.cs`.
