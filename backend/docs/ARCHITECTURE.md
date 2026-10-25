# Architecture kit — design, rules, usage

Standalone binding document for this backend kit. Use it when:

- **bootstrapping a new solution** by copying `src/Shared/`, host shells (`ApiHost` / `Gateway`), and root build/package files
- **adding a feature, module, or microservice** inside an existing solution built on this kit
- **guiding an AI agent** — read this file first; other docs under `docs/` deepen one concern and must not contradict this one

Demo, test, and production code use the **same** rules. There is no “quick inline exception.”

**Naming in this file:** `YourApp`, `ModuleA` / `ModuleB`, `ServiceA` / `ServiceB`, `Resource` / `CreateResource` are placeholders. When a snippet needs a real path from *this* repo, it is labeled **Worked example (this repo)** — copy the pattern, not the product names, into a new project.

---

## 1. What this kit is

A **.NET modular / strangler-ready** application kit:

| Idea | Meaning |
| --- | --- |
| Vertical slice | One use-case folder: HTTP → command/query → handler → (optional) validator |
| DDD where state has rules | Aggregates own invariants and raise **domain events**; handlers stay thin |
| Modular monolith | One process (`ApiHost`) hosts `YourApp.Modules.*` with separate DbContexts |
| Extracted service | Same slice + DDD patterns in `Services/YourApp.ServiceA`; talks via **integration events** |
| Shared kernel | Reusable libraries under `src/Shared/` — not a dumping ground for feature code |

Cross-boundary messaging (outbox, broker, consumers) is part of the kit. **Roles** are in §8 so agents never put `IDbContextOutbox` in a command handler.

---

## 2. What to copy into a new project

Treat these as the portable surface. Rename `YourApp` (or the sample’s product prefix) consistently across projects, namespaces, and assembly names.

### 2.1 Shared libraries (`src/Shared/`) — full tree

Copy the entire `Shared/` folder. Feature code never lands here; only kit primitives.

```text
src/Shared/
  Mediator/                          ← in-process mediator (not MediatR NuGet)
    Contracts/                       IMediator, IPublisher, IRequest, IRequestHandler,
                                     INotification, INotificationHandler, IPipelineBehavior
    Mediator.cs / Publisher.cs / registration + publish strategies
  Domain/                            ← DDD building blocks
    Entity.cs, Aggregate.cs, IAggregate.cs
    IDomainEvent.cs, DomainEvent.cs, IEntity.cs
  Common/                            ← cross-cutting primitives (no feature types)
    Errors/                          Error, ErrorOr<T>, Success, ErrorType
      Http/                          MatchOk, MatchCreated, MatchNoContent, ToProblem
    Exceptions/Http/                 shared exception → ProblemDetails
    Specifications/                  ISpecification, SpecificationBase, Apply, And/Or
    Pagination/                      PageRequest, PagedResponse, ToPagedAsync
  Application/                       ← application pipeline + persistence hooks
    Abstractions/
      Commands/                      ICommand, ICommand<T>, ICommandMarker
      Queries/                       IQuery<T>
    Behaviors/                       Logging, Validation, Caching, TransactionBehavior
    Persistence/                     AddTransactionalDbContext, IDbContextCommitFlush
  YourApp.Modular/                   ← modular-monolith host helpers
    ModuleAssemblyCatalog            discover YourApp.Modules.*
    CarterExtensions                 AddCarterWithAssemblies
    ModularEfCoreExtensions          audit interceptor registration
    IUserContext, HttpUserContext, IDataSeeder
  YourApp.Auth.Jwt/                  ← JWT bearer + permission/resource policies
    AddMyAppAuth / permissions / GetUserId / resource ownership
  YourApp.Messaging/                 ← outbox + broker wiring
    IIntegrationEvent, IntegrationEvent
    IntegrationEvents/               shared contracts (ProductCreated…, OrderPlaced…)
    MessagingQueues                  exchange / queue name constants
    Persistence/                     TransactionalDomainEventInterceptor
    UseTransactionalMessaging, WolverineDbContextCommitFlush
```

| Project | You use it for |
| --- | --- |
| **Mediator** | `IMediator.Send`, `IPublisher.Publish`, handlers, pipeline behaviors |
| **Shared.Domain** | `Aggregate`, `AddDomainEvent`, `DomainEvent` |
| **Shared.Common** | `ErrorOr`, HTTP `Match*`, specs, pagination, shared exceptions |
| **Shared.Application** | `ICommand` / `IQuery`, behaviors, transactional DbContext registration |
| **YourApp.Modular** | Module discovery, Carter helper, EF audit, seeders, user context |
| **YourApp.Auth.Jwt** | JWT validation, permission policies, ownership helpers |
| **YourApp.Messaging** | Integration contracts, outbox enroll/dispatch, Rabbit/Wolverine host wiring |

### 2.2 Host shells (copy + strip samples)

| Shell | Role after copy |
| --- | --- |
| **ApiHost** | Composition root for modules — **canonical host layout** (§9). `Program.cs` only chains `Add*` / `Use*` |
| **Gateway** | YARP routes + Scalar federating OpenAPI JSON from backends |
| **ServiceA** (any `*Api`) | Extracted BC copied from the **same host layering as ApiHost** (§9.2): `Application/` + `Infrastructure/` + `Presentation/` + feature slices. Never dump DI/messaging at the project root |

### 2.3 Build / packages (solution root)

| File | Role |
| --- | --- |
| `Directory.Build.props` | TFM, nullable, analyzers |
| `Directory.Packages.props` | Central package versions |
| `src/Modules/Directory.Build.props` | Baseline refs + global usings for every `YourApp.Modules.*` project |
| `.editorconfig` | Style consistency |

Module projects stay thin: `AssemblyName` / `RootNamespace` plus rare extras. Prefer inheriting the Modules `Directory.Build.props`.

### 2.4 How this file relates to other docs

This file is the **binding** architecture. Topic how-tos under `docs/` deepen one concern; they must not invent alternate slice shapes, host patterns, or messaging roles. If they disagree, **this file wins**.

| Doc | Scope (how-to only) |
| --- | --- |
| [`docs/auth/`](./auth/) | JWT bearer, policies, gateway/Scalar federation |
| [`docs/result-error-exception/`](./result-error-exception/) | ErrorOr / ProblemDetails mapping |
| [`docs/spec/`](./spec/) | Specifications / pagination |

---

## 3. Solution shape (generic)

```text
backend/
  Directory.Build.props
  Directory.Packages.props
  src/
    Shared/                    ← kit (copy as-is; see §2.1)
      Mediator/
      Domain/
      Common/
      Application/
      YourApp.Modular/
      YourApp.Auth.Jwt/
      YourApp.Messaging/
    Modules/                   ← YourApp.Modules.* (in-process BCs)
      Directory.Build.props
      ModuleA/
      ModuleB/
      ModuleC/
    Services/                  ← deployables
      YourApp.ApiHost/         ← hosts Modules
      YourApp.Gateway/
      YourApp.ServiceA/        ← extracted BC
      YourApp.ServiceB/
  tests/
  docs/
    ARCHITECTURE.md            ← this file
```

**Modules** live in-process with ApiHost. **Services** are separate processes. Both use the same slice + DDD + ErrorOr conventions.

---

## 4. Vertical slice — how to define each piece

Mandatory HTTP shape for every use case (modules and extracted services).

### 4.1 Folder layout

**Producer use case** (HTTP write that changes an aggregate and may raise events):

```text
{Area}/{UseCase}/                         e.g. Products/CreateProduct/
  {UseCase}Command.cs | {UseCase}Query.cs
  {UseCase}CommandHandler.cs | …
  {UseCase}Endpoint.cs                    ← ICarterModule only
  {UseCase}Validator.cs
  Events/
    {Fact}DomainEvent.cs                  ← raised by the aggregate for this use case
    Publish{Fact}IntegrationHandler.cs    ← domain→integration bridge (INotificationHandler)
    Log{Fact}Handler.cs                   ← optional in-process reaction
```

If the same domain event is raised from **more than one** use case, move it to `{Area}/Events/` (shared) instead of duplicating.

**Consumer reactions** (inbound integration events — no HTTP):

```text
Consumers/{Reaction}/                     e.g. Consumers/IndexProduct/, Consumers/ProjectProductCatalog/
  {IntegrationEvent}Handler.cs            ← Wolverine handler; idempotent
  {OtherRelatedEvent}Handler.cs           ← same reaction, another event (group by capability)
```

`Consumers/` marks inbound, message-driven work. Name `{Reaction}` after **what this BC does**, not after the producer’s folder. Group handlers for the same capability together (created + updated → one `IndexProduct/`).

Area-level types (shared across use cases of the same aggregate):

```text
{Area}/
  Resource.cs                             ← aggregate (AddDomainEvent; event types live under UseCase/Events/)
  Specifications/                         ← query filters
```

### 4.2 Command / query

```csharp
// Write that returns a value
public sealed record CreateResourceCommand(
    string Name,
    decimal Amount) : ICommand<Guid>;

// Write with no body (HTTP 204)
public sealed record UpdateResourceCommand(Guid Id, string Name) : ICommand;

// Read
public sealed record GetResourceQuery(Guid Id) : IQuery<ResourceDto>;
```

| Marker | Pipeline effect |
| --- | --- |
| `ICommand` / `ICommand<T>` | `ErrorOr` + **TransactionBehavior** saves / commits / flushes |
| `IQuery<T>` | No write commit path |

Handlers always return `ErrorOr<T>` or `ErrorOr<Success>`.

### 4.3 Validator

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

Input shape lives here. Domain invariants stay on the aggregate (`ErrorOr` from factories / mutators).

### 4.4 Endpoint (Carter) — transport only

```csharp
public sealed class CreateResourceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/resources", HandleAsync)
            .WithName("CreateResource")
            .WithTags("Resources")
            .RequireAuthorization(/* permission / policy */);
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

| HTTP outcome | Helper |
| --- | --- |
| 200 + body | `MatchOk(http)` |
| 201 + Location | `MatchCreated(http, id => $"/resources/{id}")` |
| 204 | `MatchNoContent(http)` |
| Failure | helpers map to ProblemDetails via `ToProblem` |

**Forbidden in endpoints:** `DbContext`, `SaveChanges`, business rules, `IDbContextOutbox`, broker clients.

When the body is not the command (e.g. user id from JWT), map in the endpoint then `Send`:

```csharp
var result = await mediator.Send(
    new AddItemCommand(user.GetUserId(), body.ProductId, body.Quantity), ct);
return result.MatchNoContent(http);
```

### 4.5 Handler — application orchestration

```csharp
internal sealed class CreateResourceCommandHandler(ModuleADbContext db)
    : IRequestHandler<CreateResourceCommand, ErrorOr<Guid>>
{
    public async ValueTask<ErrorOr<Guid>> Handle(
        CreateResourceCommand command,
        CancellationToken cancellationToken)
    {
        var created = Resource.Create(command.Name, command.Amount);
        if (created.IsError)
            return ErrorOr<Guid>.From(created.Errors.ToList());

        db.Resources.Add(created.Value);
        // NO SaveChanges — TransactionBehavior
        // NO outbox.PublishAsync — publisher INotificationHandler
        return created.Value.Id;
    }
}
```

Queries compose specs and project DTOs (see §6).

### 4.6 Worked example (this repo)

| Piece | Path |
| --- | --- |
| Command + handler + endpoint + validator | `Modules/Catalog/Products/CreateProduct/` |
| Query + specs + pagination | `Modules/Catalog/Products/GetProducts/` |
| Extracted-service slices | `Services/MyApp.OrdersApi/Cart/*`, `Orders/PlaceOrder/` |

---

## 5. Domain — aggregate, invariants, domain events

### 5.1 Aggregate

```csharp
public sealed class Resource : Aggregate
{
    private Resource() { } // EF

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
        // type lives in CreateResource/Events/ — aggregate references that namespace
        resource.AddDomainEvent(new ResourceCreatedDomainEvent(resource.Id, resource.Name));
        return resource;
    }

    public ErrorOr<Success> Rename(string name) { /* invariants + optional event */ }
}
```

### 5.2 Domain event type (lives under producing `{UseCase}/Events/`)

```csharp
// CreateResource/Events/ResourceCreatedDomainEvent.cs
namespace YourApp.Modules.ModuleA.Resources.CreateResource.Events;

public sealed record ResourceCreatedDomainEvent(Guid ResourceId, string Name) : DomainEvent;
```

| Rule | Detail |
| --- | --- |
| Who raises | Aggregate only (`AddDomainEvent`) |
| Where type + handlers live | `{UseCase}/Events/` for the use case that causes the fact |
| Who reacts in-process | `INotificationHandler<TDomainEvent>` in that same `Events/` folder |
| Who bridges to integration | `Publish…IntegrationHandler` in that same `Events/` folder → `outbox.PublishAsync` |
| Who triggers dispatch | Generic `TransactionalDomainEventInterceptor` on `SavingChanges` — **no event-type `switch`** |

Handlers during `SavingChanges` must **not** call `SaveChanges`. Prefer `SaveChangesAsync` when domain events may be pending (sync `SaveChanges` with pending events is rejected).

### 5.3 Worked example (this repo)

- Aggregate: `Modules/Catalog/Products/Product.cs`
- Create events: `Products/CreateProduct/Events/`
- Update events: `Products/UpdateProduct/Events/`
- Place-order events: `OrdersApi/Orders/PlaceOrder/Events/`

---

## 6. Specifications and pagination

### 6.1 Define a specification

```csharp
public sealed class ActiveResourceSpec : SpecificationBase<Resource>
{
    public ActiveResourceSpec()
    {
        Where(r => r.IsActive && !r.IsDeleted);
    }

    public static readonly ISpecification<Resource> Instance = new ActiveResourceSpec();
}

public sealed class AmountRangeSpec : SpecificationBase<Resource>
{
    public AmountRangeSpec(decimal? min, decimal? max)
    {
        if (min is { } lo) Where(r => r.Amount >= lo);
        if (max is { } hi) Where(r => r.Amount <= hi);
    }
}
```

### 6.2 Compose in a query handler

```csharp
var spec = ActiveResourceSpec.Instance;
if (filter.MinAmount is not null || filter.MaxAmount is not null)
    spec = spec.And(new AmountRangeSpec(filter.MinAmount, filter.MaxAmount));

var projected = db.Resources.Apply(spec)
    .Select(r => new ResourceListItemDto { Id = r.Id, Name = r.Name, Amount = r.Amount });

var page = await projected.ToPagedAsync(filter, ct);
return new PagedResponse<ResourceListItemDto>(page.Items, page.TotalCount, page.Page, page.PageSize);
```

Base types: `Shared.Common.Specifications` (`SpecificationBase`, `Apply`, `And` / `Or`) and `Shared.Common.Pagination` (`ToPagedAsync`, `PagedResponse`).

### 6.3 Worked example (this repo)

- Specs: `Modules/Catalog/Products/Specifications/ProductSpecs.cs`
- Handler: `Modules/Catalog/Products/GetProducts/GetProductsQueryHandler.cs`

---

## 7. Application pipeline

Registered inside each host’s **`Application/DependencyInjection.cs` → `AddApplication()`** — never inline in `Program.cs`:

1. Logging (ApiHost)  
2. Validation (FluentValidation)  
3. Caching (ApiHost; queries that opt in)  
4. **TransactionBehavior** — for `ICommandMarker`: save → commit ambient outbox tx if present → `IDbContextCommitFlush` (broker flush)

```csharp
// Application/DependencyInjection.cs
services.RegisterRequestHandlers(assemblies);
services.AddValidatorsFromAssemblies(assemblies, includeInternalTypes: true);
services.AddPipelineBehavior(typeof(LoggingBehavior<,>));
services.AddPipelineBehavior(typeof(ValidationBehavior<,>));
services.AddPipelineBehavior(typeof(CachingBehavior<,>));
services.AddPipelineBehavior(typeof(TransactionBehavior<,>));
```

DbContext registration lives in **`Infrastructure/DependencyInjection.cs` → `AddInfrastructure()`** and always attaches interceptors:

```csharp
services.AddTransactionalDbContext<ModuleADbContext>((sp, options) =>
{
    options.UseSqlite(connectionString);
    options.AddInterceptors(sp.GetServices<ISaveChangesInterceptor>());
});
```

---

## 8. Messaging — domain → integration → consume

### 8.1 Role table

| Role | Owner | Does |
| --- | --- | --- |
| Raise domain event | Aggregate | `AddDomainEvent` |
| In-process reaction | Module/service `INotificationHandler<TDomainEvent>` | Log / same-BC side effects (no cross-BC I/O) |
| Domain → integration | Publisher `INotificationHandler<TDomainEvent>` | `outbox.PublishAsync(integrationEvent)` |
| Trigger | `TransactionalDomainEventInterceptor` | Enroll + Mediator publish snapshot + clear |
| Commit + flush | `TransactionBehavior` + `WolverineDbContextCommitFlush` | Save → Commit → Flush to broker |
| Consume | Wolverine handler in **ModuleB** or **ServiceA** | Idempotent projection / side effect |
| Wire transport | `ConfigureMessaging` / `UseServiceAMessaging` | Exchanges, queues, handler discovery |

Integration contracts live in `YourApp.Messaging` (or a future `*.Contracts` assembly). Consumers never reference another BC’s domain types.

### 8.2 Define an integration event

```csharp
// in YourApp.Messaging/IntegrationEvents/
public sealed record ResourceCreatedIntegrationEvent(
    Guid ResourceId,
    string Name) : IntegrationEvent;
```

### 8.3 Publisher (bridge) handler

Lives next to the domain event under `{UseCase}/Events/`:

```csharp
// CreateResource/Events/PublishResourceCreatedIntegrationHandler.cs
namespace YourApp.Modules.ModuleA.Resources.CreateResource.Events;

public sealed class PublishResourceCreatedIntegrationHandler(IDbContextOutbox outbox)
    : INotificationHandler<ResourceCreatedDomainEvent>
{
    public async ValueTask Handle(
        ResourceCreatedDomainEvent notification,
        CancellationToken cancellationToken)
    {
        await outbox.PublishAsync(
            new ResourceCreatedIntegrationEvent(notification.ResourceId, notification.Name));
    }
}
```

### 8.4 Consumer (other module or service)

Put Wolverine handlers under **`Consumers/{Reaction}/`**, named for this BC’s side effect (not a dump `Handlers/` or `EventsConsumed/`, not a mirror of the producer path):

```text
Consumers/IndexResource/
  ResourceCreatedIntegrationEventHandler.cs
  ResourceUpdatedIntegrationEventHandler.cs
Consumers/ProjectResourceCatalog/
  ResourceCreatedIntegrationEventHandler.cs
Consumers/CreateInvoiceOnOrderPlaced/
  OrderPlacedIntegrationEventHandler.cs
```

```csharp
// Consumers/IndexResource/ResourceCreatedIntegrationEventHandler.cs
namespace YourApp.Modules.ModuleB.Consumers.IndexResource;

public sealed class ResourceCreatedIntegrationEventHandler
{
    public async Task Handle(
        ResourceCreatedIntegrationEvent message,
        ServiceADbContext db,
        CancellationToken ct)
    {
        if (await db.Projections.AnyAsync(x => x.ResourceId == message.ResourceId, ct))
            return; // idempotent

        db.Projections.Add(new ResourceProjection
        {
            ResourceId = message.ResourceId,
            Name = message.Name,
        });
        await db.SaveChangesAsync(ct);
    }
}
```

Contracts stay in `YourApp.Messaging/IntegrationEvents/` (shared). Consumers never reference another BC’s domain types.

### 8.5 Worked example (this repo)

- Contracts: `Shared/MyApp.Messaging/IntegrationEvents/`
- Publisher: `Modules/Catalog/Products/CreateProduct/Events/PublishProductCreatedIntegrationHandler.cs`
- Consumer (module): `Modules/Search/Consumers/IndexProduct/`
- Consumer (service): `OrdersApi/Consumers/ProjectProductCatalog/`, `InvoicesApi/Consumers/CreateInvoiceOnOrderPlaced/`, `ShippingApi/Consumers/CreateShipmentOnOrderPlaced/`

---

## 9. Hosts — layer isolation (ApiHost is the template)

Every deployable host (ApiHost **and** extracted `*Api` services) uses the **same three host layers**. `Program.cs` is only a composition root: chain `Add*` / `Use*` / `MapCarter`. No OpenAPI blocks, no `AddAuthorization`, no DbContext registration, no messaging wiring at the project root.

### 9.0 Layer responsibilities

| Layer folder | Extension(s) | Owns |
| --- | --- | --- |
| **`Application/`** | `AddApplication()` | Mediator handler discovery, FluentValidation, pipeline behaviors |
| **`Infrastructure/`** | `AddInfrastructure(config)`, `Use*Messaging()`, `UseInfrastructure()` | Persistence (DbContext), options, outbox/broker wiring, DB ensure/migrate on startup |
| **`Presentation/`** | `AddApi(config)`, `UseSharedPipeline()` | Auth, Carter assembly scan, OpenAPI, exception middleware, health, CORS/rate-limit (as needed) |

**Forbidden at project root:** `*MessagingExtensions.cs`, giant `Program.cs` DI, feature `MapGet`/`MapPut` handlers.

### 9.1 ApiHost tree + `Program.cs`

```text
Services/YourApp.ApiHost/
  Program.cs                         ← composition only
  Application/
    DependencyInjection.cs           → AddApplication()
  Infrastructure/
    DependencyInjection.cs           → AddInfrastructure(config)
    ApiHostMessagingExtensions.cs    → UseApiHostMessaging()
    WebApplicationExtensions.cs      → UseInfrastructure()
  Presentation/
    DependencyInjection.cs           → AddApi(config)
    WebApplicationExtensions.cs      → UseSharedPipeline()
    OpenApiAuthExtensions.cs
    …
  Features/                          ← host-only diagnostics (rare)
  Config/
```

```csharp
// Program.cs — target shape for EVERY host
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddModularEfCore()                 // ApiHost only (module audit)
    .AddApi(builder.Configuration)
    .AddModuleA(builder.Configuration)
    .AddModuleB(builder.Configuration);

builder.UseApiHostMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.UseModuleA();
app.UseModuleB();
app.MapCarter();
```

- Discover handlers/validators/Carter from `ModuleAssemblyCatalog.GetHostAndModuleAssemblies`.
- Module facade: `AddModuleA` / `UseModuleA` (+ optional `ConfigureMessaging` called from `UseApiHostMessaging`).

### 9.2 Extracted microservice (`ServiceA`) — same layers as ApiHost

Copy the **host layer folders**, not a flat DI dump. Feature code stays in area/use-case folders beside the host layers.

```text
Services/YourApp.ServiceA/
  Program.cs                         ← same shape as ApiHost (no modules)
  Application/
    DependencyInjection.cs           → AddApplication()
  Infrastructure/
    DependencyInjection.cs           → AddInfrastructure(config)   // DbContext + interceptors
    MessagingExtensions.cs           → UseServiceAMessaging()
    WebApplicationExtensions.cs      → UseInfrastructure()         // EnsureCreated / migrate
  Presentation/
    DependencyInjection.cs           → AddApi(config)              // auth, Carter, OpenAPI, exceptions
    WebApplicationExtensions.cs      → UseSharedPipeline()
    OpenApiAuthExtensions.cs
  Persistence/                       ← DbContext + EF configs (or under Infrastructure/)
  {Area}/{UseCase}/                  ← vertical slices (§4)
  Consumers/{Reaction}/              ← inbound integration handlers (§8.4)
  AssemblyMarker.cs
```

```csharp
// Program.cs — ServiceA (identical rhythm to ApiHost)
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

builder.UseServiceAMessaging();

var app = builder.Build();

app.UseInfrastructure();
app.UseSharedPipeline();
app.MapCarter();
```

| Concern | Where it goes |
| --- | --- |
| Handlers / validators / behaviors | `Application/DependencyInjection.cs` |
| `AddTransactionalDbContext` | `Infrastructure/DependencyInjection.cs` |
| Rabbit listen/publish / `IncludeHandler` | `Infrastructure/MessagingExtensions.cs` |
| JWT, fallback auth, Carter, OpenAPI | `Presentation/DependencyInjection.cs` |
| Exception middleware, `/health`, `MapOpenApi` | `Presentation/WebApplicationExtensions.cs` |
| HTTP features | `{Area}/{UseCase}/` slices — **never** `Program.cs` |

### 9.3 Module tree (in-process BC)

Modules are **not** hosts. They expose a facade the ApiHost calls; they do not own `Program.cs`.

```text
Modules/ModuleA/
  DependencyInjection.cs             → AddModuleA(config)   // DbContext, seeders
  ModuleAModule.cs (or similar)      → UseModuleA(), ConfigureMessaging(...)
  Persistence/                       ← ModuleADbContext
  {Area}/{UseCase}/                  ← vertical slices
  Consumers/{Reaction}/              ← optional inbound handlers
```

ApiHost `UseApiHostMessaging` calls each module’s `ConfigureMessaging` — modules never register Wolverine from their own `Program`.

### 9.4 Worked example (this repo)

| Host | Clean `Program.cs` | Layers |
| --- | --- | --- |
| ApiHost | `Services/MyApp.ApiHost/Program.cs` | `Application/`, `Infrastructure/` (incl. messaging), `Presentation/` |
| OrdersApi | `Services/MyApp.OrdersApi/Program.cs` | same three folders + `Cart/`, `Orders/`, `Consumers/` |
| InvoicesApi | `Services/MyApp.InvoicesApi/Program.cs` | same |
| ShippingApi | `Services/MyApp.ShippingApi/Program.cs` | same |

### 9.5 Gateway

YARP path → cluster. Scalar documents proxy each backend’s `/openapi/v1.json` and rewrite `servers` to the gateway public URL. Gateway does not validate JWT; it forwards `Authorization`.

---

## 10. How to add work (recipes)

### 10.1 New feature in an existing module or service

1. Add `{Area}/{UseCase}/` with Command|Query, Handler, Endpoint, Validator (§4).  
2. If state has rules: extend the aggregate; put `{Fact}DomainEvent` + `Publish…` (+ optional `Log…`) under **`{UseCase}/Events/`** (§5, §8).  
3. Downstream BCs: add **`Consumers/{Reaction}/`** with the integration-event handler(s) (§8.4).  
4. List/filter queries: add specs under `{Area}/Specifications/` (§6).  
5. Do **not** touch `Program.cs` except when adding a new module registration on ApiHost.  
6. Tests: unit for domain; integration for HTTP + messaging if cross-boundary.

### 10.2 New module (`YourApp.Modules.ModuleC`)

1. New project under `src/Modules/ModuleC` (inherits Modules `Directory.Build.props`).  
2. `DependencyInjection.AddModuleC` / `UseModuleC` (+ optional `ConfigureMessaging`).  
3. Own `ModuleCDbContext` + `AddTransactionalDbContext` + interceptors.  
4. Reference from ApiHost; call `AddModuleC` / `UseModuleC` in `Program.cs` only.  
5. Hook `ConfigureMessaging` from `Infrastructure/ApiHostMessagingExtensions`.

### 10.3 New microservice (`YourApp.ServiceC`)

1. Copy an existing `*Api` shell (**including** `Application/` / `Infrastructure/` / `Presentation/`). Rename assemblies/namespaces.  
2. Keep `Program.cs` as the thin chain in §9.2.  
3. Vertical slices only (§4).  
4. Gateway: route + OpenAPI proxy + Scalar document.  
5. Consume/publish via Messaging contracts — not another service’s DbContext.

---

## 11. Non-negotiables (checklist)

- [ ] HTTP surface is `ICarterModule` in a use-case folder  
- [ ] Endpoint only `mediator.Send` + `Match*` / `ToProblem`  
- [ ] Writes are `ICommand` / `ICommand<T>`; no `SaveChanges` in endpoint or (normally) handler  
- [ ] Aggregate raises domain events; domain event + publish/log handlers under `{UseCase}/Events/`  
- [ ] Integration events only from publisher notification handlers  
- [ ] Wolverine consumers under `Consumers/{Reaction}/` (named for this BC’s side effect)  
- [ ] No `switch (domainEvent)` in Shared / interceptors  
- [ ] Hosts use `Application/` + `Infrastructure/` + `Presentation/`; `Program.cs` only chains `Add*` / `Use*` / `MapCarter`  
- [ ] No root-level `*MessagingExtensions.cs`; messaging lives under `Infrastructure/`  
- [ ] Health / OpenAPI / auth middleware live in `Presentation/`, not `Program.cs`  
- [ ] Cross-BC data via integration events / explicit contracts — never shared EF entities  
- [ ] Demo and tests obey the same rules  

---

## 12. Worked examples index (this repo only)

Use these when implementing against *this* codebase. For a **new** product, follow §3–§10 with `ModuleA` / `ServiceA` names.

| Pattern | Path |
| --- | --- |
| Producer slice | `Modules/Catalog/Products/CreateProduct/` |
| Producer events | `…/CreateProduct/Events/`, `…/UpdateProduct/Events/` |
| Place-order events | `OrdersApi/Orders/PlaceOrder/Events/` |
| Query + specs + pagination | `Modules/Catalog/Products/GetProducts/` + `Products/Specifications/` |
| Aggregate | `Modules/Catalog/Products/Product.cs` |
| Consumer (module) | `Modules/Search/Consumers/IndexProduct/` |
| Consumer (service projection) | `OrdersApi/Consumers/ProjectProductCatalog/` |
| Consumer (service side effect) | `InvoicesApi/Consumers/CreateInvoiceOnOrderPlaced/`, `ShippingApi/Consumers/CreateShipmentOnOrderPlaced/` |
| Module DI + messaging configure | `Modules/Catalog/DependencyInjection.cs`, `CatalogModule.ConfigureMessaging` |
| Thin ApiHost | `Services/MyApp.ApiHost/Program.cs` + `Application/` `Infrastructure/` `Presentation/` |
| Thin OrdersApi (service template) | `Services/MyApp.OrdersApi/Program.cs` + same three host layers |
| Shared tree | `src/Shared/` (§2.1) |
| Gateway federation | `Services/MyApp.Gateway/` |

When unsure, copy the **CreateProduct** slice shape — never an inline `Program.cs` handler.
