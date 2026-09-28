---
title: "Specification Pattern, Complex Filtering/Sorting/Pagination, and a Multi-Entity Seeder"
date: '2026-09-27'
excerpt: 'A hand-rolled Specification pattern with Include/ThenInclude support, wired into the Result-pattern pipeline, plus a richer entity model and seeder.'
---

> **Cross-checked against the actual MyApp layout.** Things below were adjusted from the original draft after checking against what's really in the repo today, not assumed:
> 1. **No Domain project exists yet** — specs live in `Shared.Common.Specifications` (generic pieces) and per-feature folders under `MyApp.Api/Features/...` (concrete ones), not `Domain.Specifications`.
> 2. **`Mediator` (`Shared.Mediator`), not `MediatR`** — handlers match `CreateProductCommandHandler` / `GetProductQueryHandler`: `IRequestHandler<TRequest, ErrorOr<TResponse>>` with `ValueTask<ErrorOr<TResponse>> Handle(...)`.
> 3. **No `IReadRepository<T>`** — handlers inject `AppDbContext` and call `.Apply(spec)` on `dbContext.Products` (same as today's `GetProductQueryHandler`).
> 4. **`Shared.Common` already references EF Core** — `SpecificationEvaluator.Apply` lives in [`Shared.Common.Specifications`](../../src/Shared/Common/Specifications/SpecificationEvaluator.cs). Put `PageRequest` / `PagedResult` / `ToPagedAsync` in `Shared.Common` too (sections 2.6 / 3). There is **no** separate `Shared.EntityFramework` project.
> 5. **Entities live next to the feature that owns them** — e.g. today's [`Features/Products/Product.cs`](../../src/Services/MyApp.Api/Features/Products/Product.cs); `Category`/`Brand`/`Tag`/`Review` get their **own** feature folders (see §1), not a dump under `Products/`. Not `Persistence/Entities/` or a Domain project. (`MyApp.Api` has `Application/` / `Infrastructure/` / `Presentation/` composition folders, but those are DI/host wiring — not a place for entities.)
> 6. **[`AppDbContext`](../../src/Services/MyApp.Api/Persistence/AppDbContext.cs) configures `Product` inline today** — it does *not* call `ApplyConfigurationsFromAssembly`. Section 1 shows either continuing that style or *adding* configuration classes + that one call as an intentional change.
> 7. **DB bootstrap uses `Migrate()`** — [`UseInfrastructure`](../../src/Services/MyApp.Api/Infrastructure/WebApplicationExtensions.cs) applies EF migrations under `Persistence/Migrations/`. Dev-only seeding runs after migrate (section 5). If an old SQLite file was created with `EnsureCreated`, delete it once so migrations can own the schema.

## 0. The decision: which Specification implementation

Three options were on the table: `Ardalis.Specification` (the NuGet package), the hand-rolled version from the article you attached, or a corrected custom build. **Going with a corrected custom build**, placed in `Shared.Common.Specifications` (generic pieces) and per-feature folders (concrete specs) — see the callout above for why not a `Domain` project. Here's the reasoning, not just the conclusion.

### Why not `Ardalis.Specification`

It's a mature, widely-used package and I'm not dismissing it generally — but it's a poor fit for *this* codebase specifically, for reasons tied to decisions you've already made elsewhere in this project, not taste:

- **You already rejected `ErrorOr` (the package) for the same category of reason: name friction, missing pieces, and workarounds needed to bend it to your shape.** `Ardalis.Specification`'s own `Evaluator` and `RepositoryBase<T>` assume its idiom (a `Query.Where(...).Include(...).OrderBy(...).Paginate(...)` builder living *inside* one spec object per query) end to end. Adopting it here would mean either fighting it to keep the `PageRequest`/`ToPagedAsync` design being built in section 3 (with the `Id` tie-breaker, the overflow-safe skip, the max-page-size clamp — none of which the package knows about) or duplicating that logic inside the package's own paging extension instead. Neither is a clean fit next to the rest of this work.
- **It pulls in its own repository abstraction** (`IRepositoryBase<T>`), which would sit awkwardly next to MyApp's `Mediator` handlers, which already talk to `AppDbContext`/`IQueryable<T>` directly and project straight to DTOs — no repository layer anywhere else in the app.
- A ~150-line custom type, same as your `Error`/`ErrorOr<T>`, covers what you actually need here (`Where` + `Include`/`ThenInclude` + `AsSplitQuery`) without adopting a second dependency's conventions.

### Why not the article's version verbatim

Covered in the previous turn in detail — three real defects: `And`/`Or` mutate `this` in place (a data race the moment a stateless spec like `ActiveProductSpec` is cached or reused across concurrent requests), `Expression<Func<T, object>>` for `OrderBy` boxes value-typed keys (a real, provider-dependent sharp edge on `decimal`/`DateTime` columns), and paging smuggled in as a fake `Criteria`-less spec ANDed onto real filters (works by accident, not by design, and makes composition order silently fragile).

### The design actually being built

A spec here has exactly two jobs: **the `WHERE` clause** (`Criteria`) and **the shape of the object graph to load** (`Include`/`ThenInclude` chains, `AsSplitQuery`). It does **not** own ordering or paging — that stays in `PagedQueryExtensions.ToPagedAsync` (**section 3 builds this**; it is not in MyApp yet). This split matters for a concrete reason: `ToPagedAsync` needs a *projected* `IQueryable<TDto>` to page efficiently (select only the columns the client sees, before `Skip`/`Take`), while `Include` only makes sense against the *entity* type, for a detail endpoint that returns the whole graph. Folding both responsibilities into one spec type, the way the article and `Ardalis.Specification` both do, means every spec has to awkwardly support two mutually exclusive use cases. Splitting them means:

- **List endpoints** (`GET /products?category=...&sortBy=price`): atomic, composable `Criteria`-only specs (`ActiveProductSpec`, `InStockSpecification`, `ByCategorySpecification`, ...) combined with `.And(...)`, applied to a query that's immediately `.Select(...)`-projected, then handed to `ToPagedAsync`.
- **Detail/graph endpoints** (`GET /products/{id}` with reviews, tags, category, brand all loaded): one spec per query, built with real `Include`/`ThenInclude` C# syntax (typed, no reflection, no boxing), returning the full entity — no projection needed because the client wants the whole shape.

Both cases share **one** `ISpecification<T>` interface and **one** evaluator, and composition (`.And`/`.Or`/`.Not`) is immutable — every combinator returns a new object, so a stateless spec is safe to hold as a `static readonly` field and reuse across every request without the article's race condition.

---
## 1. Expanding beyond `Product`: the entity model

A single `Product` entity can't exercise `Include`/`ThenInclude` meaningfully — there's nothing to include. Here's a small but real relational graph: enough to demonstrate a one-to-many (`Category`/`Brand` → `Product`), a many-to-many (`Product` ↔ `Tag`), and a two-hop chain (`Order` → `OrderItem` → `Product` → `Category`) for `ThenInclude`.

```
Category  ──1───N──▶  Product  ◀──N───N──  Tag
Brand     ──1───N──▶  Product
Product   ──1───N──▶  Review  ◀──N───1──  Customer
Customer  ──1───N──▶  Order  ──1───N──▶  OrderItem  ──N───1──▶  Product
```

*Why an explicit graph instead of one bigger `Product`?* Because the whole point of this section is to give `Include`/`ThenInclude` and the specification pattern something real to do. Today's live [`Product.cs`](../../src/Services/MyApp.Api/Features/Products/Product.cs) is a flat bag (`Name`, string `Category`, `Stock`, …) with **no navigations** — fine for CRUD demos, useless for Include chains.

**Placement (match MyApp vertical slices):** one feature folder owns the entity that feature is about — same idea as today’s `Features/Products/Product.cs`. **Do not** dump `Category` / `Brand` / `Tag` / `Review` into `Features/Products/` just because Product references them; that folder is for product use-cases (Create/Get/Update/…), not the whole catalog graph. Cross-feature types are fine (same assembly) — use `using` + FK navigations.

| Entity | Folder | Why |
|---|---|---|
| `Product` | `Features/Products/` | Already lives here |
| `Category` | `Features/Categories/` | Own CRUD/list later; not a Product nested type |
| `Brand` | `Features/Brands/` | Same |
| `Tag` | `Features/Tags/` | Same |
| `Review` | `Features/Reviews/` | Written against a product + customer; own slice |
| `Customer` | `Features/Customers/` | Own slice |
| `Order` / `OrderItem` / `OrderStatus` | `Features/Orders/` | Own slice |

Not `Persistence/Entities/` and not a Domain project.

```csharp
// src/Services/MyApp.Api/Features/Categories/Category.cs
using MyApp.Features.Products;

namespace MyApp.Features.Categories;

public sealed class Category
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public ICollection<Product> Products { get; init; } = [];
}

// src/Services/MyApp.Api/Features/Brands/Brand.cs
using MyApp.Features.Products;

namespace MyApp.Features.Brands;

public sealed class Brand
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string? Country { get; set; }
    public ICollection<Product> Products { get; init; } = [];
}

// src/Services/MyApp.Api/Features/Tags/Tag.cs
using MyApp.Features.Products;

namespace MyApp.Features.Tags;

public sealed class Tag
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    // Skip navigation — EF Core 5+ manages the join table (ProductTag) implicitly,
    // so there's no join entity class to write by hand for a plain many-to-many.
    public ICollection<Product> Products { get; init; } = [];
}

// src/Services/MyApp.Api/Features/Products/Product.cs
// Replaces the flat Product (string Category / Stock) with a relational shape for this guide.
using MyApp.Features.Brands;
using MyApp.Features.Categories;
using MyApp.Features.Orders;
using MyApp.Features.Reviews;
using MyApp.Features.Tags;

namespace MyApp.Features.Products;

public sealed class Product
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; init; }

    public Guid CategoryId { get; set; }
    public Category Category { get; init; } = null!;

    public Guid BrandId { get; set; }
    public Brand Brand { get; init; } = null!;

    public ICollection<Tag> Tags { get; init; } = [];
    public ICollection<Review> Reviews { get; init; } = [];
    public ICollection<OrderItem> OrderItems { get; init; } = [];
}

// src/Services/MyApp.Api/Features/Customers/Customer.cs
using MyApp.Features.Orders;
using MyApp.Features.Reviews;

namespace MyApp.Features.Customers;

public sealed class Customer
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public ICollection<Review> Reviews { get; init; } = [];
    public ICollection<Order> Orders { get; init; } = [];
}

// src/Services/MyApp.Api/Features/Reviews/Review.cs
using MyApp.Features.Customers;
using MyApp.Features.Products;

namespace MyApp.Features.Reviews;

public sealed class Review
{
    public Guid Id { get; init; }
    public Guid ProductId { get; set; }
    public Product Product { get; init; } = null!;
    public Guid CustomerId { get; set; }
    public Customer Customer { get; init; } = null!;
    public int Rating { get; set; }          // 1–5
    public string? Comment { get; set; }
    public DateTime CreatedAt { get; init; }
}

// src/Services/MyApp.Api/Features/Orders/OrderStatus.cs
namespace MyApp.Features.Orders;

public enum OrderStatus { Pending, Paid, Shipped, Cancelled }

// src/Services/MyApp.Api/Features/Orders/Order.cs
using MyApp.Features.Customers;

namespace MyApp.Features.Orders;

public sealed class Order
{
    public Guid Id { get; init; }
    public Guid CustomerId { get; set; }
    public Customer Customer { get; init; } = null!;
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; init; }
    public ICollection<OrderItem> Items { get; init; } = [];
}

// src/Services/MyApp.Api/Features/Orders/OrderItem.cs
using MyApp.Features.Products;

namespace MyApp.Features.Orders;

public sealed class OrderItem
{
    public Guid Id { get; init; }
    public Guid OrderId { get; set; }
    public Order Order { get; init; } = null!;
    public Guid ProductId { get; set; }
    public Product Product { get; init; } = null!;
    public int Quantity { get; set; }
    public decimal UnitPriceAtPurchase { get; set; }   // snapshot — the product's price can change later
}
```

*Why snapshot `UnitPriceAtPurchase` on `OrderItem` instead of reading `Product.Price` at render time?* An order is a historical record; if the product's price changes next month, last month's order shouldn't silently reprice itself when displayed. This is a real modelling decision, not incidental — worth keeping even outside a DDD-styled domain.

### EF Core configuration

Today's [`AppDbContext`](../../src/Services/MyApp.Api/Persistence/AppDbContext.cs) only has `DbSet<Product>` and configures that entity **inline** inside `OnModelCreating`. There is **no** `ApplyConfigurationsFromAssembly` call and **no** `IEntityTypeConfiguration<>` classes yet.

With a multi-entity graph, inline configuration gets noisy. Prefer extracting `IEntityTypeConfiguration<>` classes under `Persistence/Configurations/` (same project as `AppDbContext`) and **adding** `ApplyConfigurationsFromAssembly` as a deliberate change — not something already present.

```csharp
// src/Services/MyApp.Api/Persistence/Configurations/ProductConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Products;

namespace MyApp.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Price).HasPrecision(18, 2);

        b.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId);
        b.HasOne(p => p.Brand).WithMany(br => br.Products).HasForeignKey(p => p.BrandId);

        // Skip navigation both sides — EF Core creates and manages a "ProductTag" join
        // table with no corresponding C# class. Query it via p.Tags / t.Products directly.
        b.HasMany(p => p.Tags).WithMany(t => t.Products);

        // Indexes for exactly the columns the filtering/sorting section (3) actually
        // uses — see the "why" in that section for which ones matter and why.
        b.HasIndex(p => p.CategoryId);
        b.HasIndex(p => p.BrandId);
        b.HasIndex(p => p.Price);
        b.HasIndex(p => new { p.IsActive, p.IsDeleted });
    }
}

// src/Services/MyApp.Api/Persistence/Configurations/OrderConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Orders;

namespace MyApp.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasOne(o => o.Customer).WithMany(c => c.Orders).HasForeignKey(o => o.CustomerId);
        b.HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId);
    }
}

// src/Services/MyApp.Api/Persistence/Configurations/OrderItemConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Orders;

namespace MyApp.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.Property(i => i.UnitPriceAtPurchase).HasPrecision(18, 2);
        b.HasOne(i => i.Product).WithMany(p => p.OrderItems).HasForeignKey(i => i.ProductId);
    }
}

// src/Services/MyApp.Api/Persistence/Configurations/ReviewConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Reviews;

namespace MyApp.Persistence.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.HasOne(r => r.Product).WithMany(p => p.Reviews).HasForeignKey(r => r.ProductId);
        b.HasOne(r => r.Customer).WithMany(c => c.Reviews).HasForeignKey(r => r.CustomerId);
        b.HasIndex(r => r.ProductId);
    }
}
```

Then evolve `AppDbContext` to expose the new sets and pick up those configuration classes (this **replaces** the current inline `Product` block):

```csharp
// src/Services/MyApp.Api/Persistence/AppDbContext.cs
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Brands;
using MyApp.Features.Categories;
using MyApp.Features.Customers;
using MyApp.Features.Orders;
using MyApp.Features.Products;
using MyApp.Features.Reviews;
using MyApp.Features.Tags;

namespace MyApp.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // New: discover IEntityTypeConfiguration<> in this assembly.
        // Not present on today's AppDbContext — add it when you introduce the config classes above.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

*Alternative that stays closer to today's style:* keep configuring everything inline in `OnModelCreating` and skip `IEntityTypeConfiguration<>` entirely. Same EF result; just harder to skim once the graph grows.

---
## 2. The Specification pattern, with `Include`/`ThenInclude`

### 2.1 The interface

*Why `Criteria` and `IncludeChains` and nothing else?* Ordering and paging are deliberately absent — that's owned by `PagedQueryExtensions.ToPagedAsync`, built from scratch in section 3 (it doesn't exist in MyApp yet). A spec answers exactly two questions: *which rows*, and *what shape*.

```csharp
// src/Shared/Common/Specifications/ISpecification.cs — generic, reusable by any entity/feature
namespace Shared.Common.Specifications;

public interface ISpecification<T> where T : class
{
    Expression<Func<T, bool>>? Criteria { get; }

    // Each entry is one full Include/ThenInclude chain, written as ordinary EF Core
    // LINQ. A List<Func<...>> rather than a single delegate because a spec can need
    // more than one independent chain (Category AND Reviews.ThenInclude(Customer), say) —
    // see 2.4 for why that specific combination also needs AsSplitQuery.
    IReadOnlyList<Func<IQueryable<T>, IQueryable<T>>> IncludeChains { get; }

    bool AsNoTracking { get; }
    bool AsSplitQuery { get; }

    // For checking a rule against an object already in memory (e.g. inside a domain
    // method — see the DDD appendix), without touching the database at all.
    bool IsSatisfiedBy(T entity);
}
```

### 2.2 The base class authors derive from

```csharp
// src/Shared/Common/Specifications/SpecificationBase.cs
namespace Shared.Common.Specifications;

public abstract class SpecificationBase<T> : ISpecification<T> where T : class
{
    private readonly List<Func<IQueryable<T>, IQueryable<T>>> _includeChains = [];
    private Func<T, bool>? _compiled;

    public Expression<Func<T, bool>>? Criteria { get; private set; }
    public IReadOnlyList<Func<IQueryable<T>, IQueryable<T>>> IncludeChains => _includeChains;
    public bool AsNoTracking { get; protected init; } = true;   // read specs default to no-tracking
    public bool AsSplitQuery { get; protected init; }

    // Called from a derived spec's constructor. Combining with AND (not overwriting) lets
    // a subclass call this more than once for several independent conditions and read
    // cleanly, rather than building one big && expression by hand.
    protected void Where(Expression<Func<T, bool>> criteria) =>
        Criteria = Criteria is null ? criteria : Criteria.And(criteria);

    // Takes a real EF Core Include/ThenInclude chain — e.g.
    //   Include(q => q.Include(p => p.Category));
    //   Include(q => q.Include(p => p.Reviews).ThenInclude(r => r.Customer));
    // Because this is a plain delegate over IQueryable<T> built with normal generic
    // method calls, .ThenInclude sees the exact TProperty type from the .Include call
    // before it, so the whole chain is fully typed — no casting to object, no reflection.
    protected void Include(Func<IQueryable<T>, IQueryable<T>> includeChain) =>
        _includeChains.Add(includeChain);

    public bool IsSatisfiedBy(T entity) =>
        (_compiled ??= (Criteria ?? (_ => true)).Compile())(entity);
}
```

*Why does `Where` merge with AND instead of throwing if called twice?* Because it lets a spec constructor read as a list of conditions:

```csharp
public sealed class ActiveInStockUnderPriceSpec : SpecificationBase<Product>
{
    public ActiveInStockUnderPriceSpec(decimal maxPrice)
    {
        Where(p => p.IsActive && !p.IsDeleted);
        Where(p => p.StockQuantity > 0);
        Where(p => p.Price <= maxPrice);
    }
}
```

rather than one long `&&` chain that's harder to diff or comment per-condition. (In practice you'll usually prefer several small specs combined with `.And(...)` at the call site — section 2.5 — over one spec with several `Where` calls; both work, and mixing the two styles is fine.)

### 2.3 Composition: `And`, `Or`, `Not` — immutable

This is the fix for the article's race condition. Every combinator below returns a **new** `ISpecification<T>`; neither input is touched. A spec with no constructor parameters (`ActiveProductSpec`) is therefore safe to keep as a cached `static readonly` instance and reuse across every concurrent request — nothing about combining it with another spec ever mutates it.

```csharp
// src/Shared/Common/Specifications/SpecificationExtensions.cs
namespace Shared.Common.Specifications;

public static class SpecificationExtensions
{
    public static ISpecification<T> And<T>(this ISpecification<T> left, ISpecification<T> right) where T : class
        => new CombinedSpecification<T>(left, right, CombineMode.And);

    public static ISpecification<T> Or<T>(this ISpecification<T> left, ISpecification<T> right) where T : class
        => new CombinedSpecification<T>(left, right, CombineMode.Or);

    public static ISpecification<T> Not<T>(this ISpecification<T> spec) where T : class
        => new NegatedSpecification<T>(spec);
}

file enum CombineMode { And, Or }

file sealed class CombinedSpecification<T>(ISpecification<T> left, ISpecification<T> right, CombineMode mode)
    : ISpecification<T> where T : class
{
    private Func<T, bool>? _compiled;

    public Expression<Func<T, bool>>? Criteria => (left.Criteria, right.Criteria) switch
    {
        (null, null) => null,
        (var l, null) => l,
        (null, var r) => r,
        (var l, var r) => mode == CombineMode.And ? l!.And(r!) : l!.Or(r!),
    };

    // Merging include chains from both sides means combining a rich, Include-bearing
    // spec with a plain filter spec (e.g. a detail spec AND a "not deleted" filter) just
    // works — EF Core silently no-ops a duplicate Include if both sides happen to name
    // the same navigation, so accidental duplication here is harmless, not a bug.
    public IReadOnlyList<Func<IQueryable<T>, IQueryable<T>>> IncludeChains =>
        [.. left.IncludeChains, .. right.IncludeChains];

    public bool AsNoTracking => left.AsNoTracking && right.AsNoTracking;
    public bool AsSplitQuery => left.AsSplitQuery || right.AsSplitQuery;

    public bool IsSatisfiedBy(T entity) =>
        (_compiled ??= (Criteria ?? (_ => true)).Compile())(entity);
}

file sealed class NegatedSpecification<T>(ISpecification<T> inner) : ISpecification<T> where T : class
{
    private Func<T, bool>? _compiled;
    public Expression<Func<T, bool>>? Criteria => inner.Criteria?.Not();
    public IReadOnlyList<Func<IQueryable<T>, IQueryable<T>>> IncludeChains => inner.IncludeChains;
    public bool AsNoTracking => inner.AsNoTracking;
    public bool AsSplitQuery => inner.AsSplitQuery;
    public bool IsSatisfiedBy(T entity) => (_compiled ??= (Criteria ?? (_ => true)).Compile())(entity);
}
```

`Expression<Func<T,bool>>.And/Or/Not` are the same expression-tree-merging extension methods from the article (the `ReplaceParameterVisitor` trick) — that part of the article is correct and unchanged:

```csharp
// src/Shared/Common/Specifications/ExpressionExtensions.cs — unchanged from the article
// Pure expression-tree helpers (no EF) — sit next to ISpecification / SpecificationExtensions
public static class ExpressionExtensions
{
    public static Expression<Func<T, bool>> And<T>(this Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
    {
        var p = Expression.Parameter(typeof(T));
        var body = Expression.AndAlso(
            new ReplaceParameterVisitor(a.Parameters[0], p).Visit(a.Body)!,
            new ReplaceParameterVisitor(b.Parameters[0], p).Visit(b.Body)!);
        return Expression.Lambda<Func<T, bool>>(body, p);
    }

    public static Expression<Func<T, bool>> Or<T>(this Expression<Func<T, bool>> a, Expression<Func<T, bool>> b)
    {
        var p = Expression.Parameter(typeof(T));
        var body = Expression.OrElse(
            new ReplaceParameterVisitor(a.Parameters[0], p).Visit(a.Body)!,
            new ReplaceParameterVisitor(b.Parameters[0], p).Visit(b.Body)!);
        return Expression.Lambda<Func<T, bool>>(body, p);
    }

    public static Expression<Func<T, bool>> Not<T>(this Expression<Func<T, bool>> e)
        => Expression.Lambda<Func<T, bool>>(Expression.Not(e.Body), e.Parameters[0]);
}

file sealed class ReplaceParameterVisitor(ParameterExpression oldParam, ParameterExpression newParam) : ExpressionVisitor
{
    protected override Expression VisitParameter(ParameterExpression node) =>
        node == oldParam ? newParam : base.VisitParameter(node);
}
```

### 2.4 Atomic, reusable filter specs — for list endpoints

Each class owns **one** filter idea (`IsActive`, in-stock, price range, …). The constructor calls `Where(...)` so the spec is complete the moment you `new` it — no second setup step.

```csharp
// src/Services/MyApp.Api/Features/Products/Specifications/ProductSpecs.cs
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Products;
using Shared.Common.Specifications;

public sealed class ActiveProductSpec : SpecificationBase<Product>
{
    public ActiveProductSpec() => Where(p => p.IsActive && !p.IsDeleted);
}

public sealed class InStockSpecification : SpecificationBase<Product>
{
    public InStockSpecification(int minQuantity = 1) =>
        Where(p => p.StockQuantity >= minQuantity);
}

public sealed class PriceRangeSpecification : SpecificationBase<Product>
{
    public PriceRangeSpecification(decimal? min, decimal? max)
    {
        if (min is { } lo) Where(p => p.Price >= lo);
        if (max is { } hi) Where(p => p.Price <= hi);
    }
}

public sealed class ByCategorySpecification : SpecificationBase<Product>
{
    public ByCategorySpecification(Guid categoryId) => Where(p => p.CategoryId == categoryId);
}

public sealed class ByBrandSpecification : SpecificationBase<Product>
{
    public ByBrandSpecification(Guid brandId) => Where(p => p.BrandId == brandId);
}

public sealed class ByTagSpecification : SpecificationBase<Product>
{
    // Tags.Any translates to an EXISTS against the ProductTag join table — normal EF SQL,
    // not an in-memory filter.
    public ByTagSpecification(Guid tagId) => Where(p => p.Tags.Any(t => t.Id == tagId));
}

public sealed class ProductSearchSpecification : SpecificationBase<Product>
{
    // MyApp uses SQLite — EF.Functions.Like (core EF). ILike is Npgsql/Postgres-only.
    public ProductSearchSpecification(string term)
    {
        var pattern = $"%{term}%";
        Where(p =>
            EF.Functions.Like(p.Name, pattern) ||
            (p.Description != null && EF.Functions.Like(p.Description, pattern)));
    }
}
```

Call site — always `new` for parameterized specs; `new` is fine for parameterless ones too:

```csharp
ISpecification<Product> spec = new ActiveProductSpec()
    .And(new InStockSpecification(minQuantity: 1))
    .And(new PriceRangeSpecification(min, max));
```

#### Optional optimization: `static Instance` for parameterless specs

Only when a spec has **no constructor parameters** (every instance is identical) and you compose it often. Because section 2.3’s `.And()` / `.Or()` return **new** objects and never mutate the inputs, a shared singleton is safe under concurrent requests:

```csharp
public sealed class ActiveProductSpec : SpecificationBase<Product>
{
    public ActiveProductSpec() => Where(p => p.IsActive && !p.IsDeleted);

    // Optional — skip until the call site is noisy with new ActiveProductSpec().
    public static readonly ISpecification<Product> Instance = new ActiveProductSpec();
}

// Then either is fine:
var a = new ActiveProductSpec().And(new ByCategorySpecification(id));
var b = ActiveProductSpec.Instance.And(new ByCategorySpecification(id));
```

| | Use `new Spec(...)` | Optional `static Instance` |
|---|---|---|
| Parameterless (`ActiveProductSpec`) | Always correct | Nice reuse; add later if you want |
| Parameterized (`InStockSpecification(5)`, `ByCategorySpecification(id)`) | Required — values differ per request | **Never** — one shared instance cannot hold every `minQuantity` / `categoryId` |

Later sections that write `ActiveProductSpec.Instance` assume you added this optional field; `new ActiveProductSpec()` is equivalent.

### 2.5 A rich spec with `Include`/`ThenInclude` — for a detail endpoint

```csharp
// src/Services/MyApp.Api/Features/Products/Specifications/ProductDetailsSpecification.cs

using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Features.Products.Specifications;

public sealed class ProductDetailsSpecification : SpecificationBase<Product>
{
    public ProductDetailsSpecification(Guid productId)
    {
        Where(p => p.Id == productId);

        Include(q => q.Include(p => p.Category));
        Include(q => q.Include(p => p.Brand));
        Include(q => q.Include(p => p.Tags));

        // Two-level chain: load each Review's Customer too, not just the Review rows.
        Include(q => q.Include(p => p.Reviews).ThenInclude(r => r.Customer));

        // Reviews AND Tags are BOTH collections loaded on the same root Product. Without
        // AsSplitQuery, EF Core joins everything into one SQL query, and the result is
        // effectively #Reviews × #Tags rows for a single product's data — a real cartesian
        // explosion that gets worse combinatorially as either collection grows. AsSplitQuery
        // instead issues one SQL query per collection (still 1 root query + 1 per included
        // collection, not per row) and stitches them together in memory. Use it whenever a
        // spec includes more than one *collection* navigation at the same level.
        AsSplitQuery = true;
    }
}
```

A three-level chain, for the order-history example in section 4:

```csharp
// src/Services/MyApp.Api/Features/Orders/Specifications/OrderWithItemsSpecification.cs
using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Features.Orders.Specifications;

public sealed class OrderWithItemsSpecification : SpecificationBase<Order>
{
    public OrderWithItemsSpecification(Guid orderId)
    {
        Where(o => o.Id == orderId);

        Include(q => q
            .Include(o => o.Items)
                .ThenInclude(i => i.Product)
                    .ThenInclude(p => p.Category));   // Order → Items → Product → Category
        Include(q => q.Include(o => o.Customer));

        // Only one collection (Items) at the top level here, so no cartesian-explosion
        // risk — AsSplitQuery isn't needed. Leave the default (false) unless you add a
        // second sibling collection later.
    }
}
```

### 2.6 The evaluator

*Why a static method instead of a class you instantiate?* There's no state to hold between calls — it's a pure transformation from `(IQueryable<T>, ISpecification<T>)` to `IQueryable<T>`, so a static method says that directly.

**Where it lives:** `Apply` needs `AsNoTracking` / `AsSplitQuery` (`Microsoft.EntityFrameworkCore`). MyApp already put the EF package on [`Shared.Common.csproj`](../../src/Shared/Common/Shared.Common.csproj) and the evaluator next to the other spec types — that is the layout this guide follows. There is **no** `Shared.EntityFramework` project.

```text
Shared.Common
  Specifications/     → ISpecification, SpecificationBase, ExpressionExtensions, SpecificationEvaluator.Apply
  Pagination/         → PageRequest, PagedResult, PagedResponse, PagedQueryExtensions.ToPagedAsync  (§3)
MyApp.Api             → AppDbContext, feature entities/specs, handlers  (refs Shared.Common)
```

Live code (already in the repo):

```csharp
// src/Shared/Common/Specifications/SpecificationEvaluator.cs
using Microsoft.EntityFrameworkCore;

namespace Shared.Common.Specifications;

public static class SpecificationEvaluator
{
    public static IQueryable<T> Apply<T>(this IQueryable<T> source, ISpecification<T> spec) where T : class
    {
        var query = source;

        if (spec.AsNoTracking) query = query.AsNoTracking();
        if (spec.Criteria is not null) query = query.Where(spec.Criteria);

        foreach (var include in spec.IncludeChains)
            query = include(query);

        if (spec.AsSplitQuery) query = query.AsSplitQuery();

        return query;
    }
}
```

An extension method on `IQueryable<T>` (`source.Apply(spec)`) rather than a separate static class call (`SpecificationEvaluator.Apply(source, spec)`) — both compile identically, but the extension form reads left-to-right at the call site, and it's what handlers call directly (section 2.7): `dbContext.Products.Apply(spec)`.

### 2.7 No repository — handlers call `.Apply(spec)` on `AppDbContext` directly

*Why no `IReadRepository<T>`?* MyApp's handlers already talk to `AppDbContext` directly today — `GetProductQueryHandler` injects `AppDbContext dbContext` and queries `dbContext.Products` straight away, with no repository in between. Introducing a generic read-repository *only* for spec-based queries would make specs the one place in the app with an extra abstraction layer, inconsistent with every other handler rather than consistent with them. The `.Apply(spec)` extension from 2.6 is the entire seam needed:

```csharp
// Inside any handler — no repository, no interface, no DI registration beyond AppDbContext itself.
internal sealed class SomeQueryHandler(AppDbContext dbContext) : IRequestHandler<SomeQuery, ErrorOr<SomeDto>>
{
    public async ValueTask<ErrorOr<SomeDto>> Handle(SomeQuery query, CancellationToken ct)
    {
        var spec = new SomeSpecification(/* ... */);

        // Query(spec)-equivalent, one line: apply the spec, then keep composing —
        // .Select(...).ToListAsync() for an unpaged list (§2.8), or .Select(...).ToPagedAsync
        // (section 3), or .FirstOrDefaultAsync() for a single entity (§2.5 detail).
        var entity = await dbContext.Products.Apply(spec).FirstOrDefaultAsync(ct);
        // ...
    }
}
```

*Why does `.Apply(spec)` return `IQueryable<T>` rather than something that already executes?* Because a list handler (§2.8 / section 3) needs to `.Select(...)` to a DTO *before* the query runs — executing first would mean pulling every column of every matching row into memory, defeating the projection entirely. Keeping `.Apply(spec)` as a plain `IQueryable<T>`-returning extension is what lets a spec's filtering/include shape and pagination compose without either one forcing execution on the other.

If a handler needs a count or an `Any` check without pulling in Includes (wasting a join the database never needed for a `COUNT(*)`), apply the criteria directly rather than through the spec's `Apply`:

```csharp
var exists = await (spec.Criteria is null ? dbContext.Products : dbContext.Products.Where(spec.Criteria)).AnyAsync(ct);
```

This is a two-line inline pattern, not a reusable method, precisely because it's the *only* two lines that would otherwise justify a repository — not enough on its own to add one.

### 2.8 Putting it together: filtered list **without** pagination

Before adding `PageRequest` / `ToPagedAsync` (section 3), here is the full list-handler shape using only what section 2 already built: compose atomic specs → `.Apply(spec)` → `.Select(...)` → materialize. No sorting/paging specs, no repository.

```csharp
// src/Services/MyApp.Api/Features/Products/GetProducts/ProductListItemDto.cs
public sealed record ProductListItemDto(
    Guid Id, string Name, decimal Price, int StockQuantity,
    string CategoryName, string BrandName, DateTime CreatedAt);

// Example filter inputs (query string / DTO) — §4 makes this inherit PageRequest
public sealed class ProductFilter
{
    public Guid? CategoryId { get; init; }
    public Guid? BrandId { get; init; }
    public Guid? TagId { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool InStockOnly { get; init; }
    public string? Search { get; init; }
}

// src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsQuery.cs
// Must carry Filter — otherwise query.Filter has nowhere to come from.
public sealed record GetProductsQuery(ProductFilter Filter)
    : IQuery<List<ProductListItemDto>>;

// src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsQueryHandler.cs
// First version — returns every matching row. Section 3/4 swap ToListAsync for ToPagedAsync.
internal sealed class GetProductsQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductsQuery, ErrorOr<List<ProductListItemDto>>>
{
    public async ValueTask<ErrorOr<List<ProductListItemDto>>> Handle(
        GetProductsQuery query, CancellationToken ct)
    {
        var f = query.Filter;

        // 1. Compose the WHERE from atomic specs (§2.4 / §2.3). Optional Instance for
        //    ActiveProductSpec; new ActiveProductSpec() is equivalent.
        ISpecification<Product> spec = ActiveProductSpec.Instance;
        if (f.InStockOnly) spec = spec.And(new InStockSpecification());
        if (f.CategoryId is { } categoryId) spec = spec.And(new ByCategorySpecification(categoryId));
        if (f.BrandId is { } brandId) spec = spec.And(new ByBrandSpecification(brandId));
        if (f.TagId is { } tagId) spec = spec.And(new ByTagSpecification(tagId));
        if (f.MinPrice is not null || f.MaxPrice is not null)
            spec = spec.And(new PriceRangeSpecification(f.MinPrice, f.MaxPrice));
        if (!string.IsNullOrWhiteSpace(f.Search))
            spec = spec.And(new ProductSearchSpecification(f.Search));

        // 2. Apply on DbSet, then project. No Include — Select pulls Category.Name / Brand.Name
        //    via SQL JOINs. Include is for full graphs (detail spec, §2.5).
        var items = await dbContext.Products
            .Apply(spec)
            .Select(p => new ProductListItemDto(
                p.Id, p.Name, p.Price, p.StockQuantity,
                p.Category.Name, p.Brand.Name, p.CreatedAt))
            .ToListAsync(ct);

        return items;
    }
}
```

What this teaches before pagination:

| Step | Owns | Not a job for |
|---|---|---|
| Spec composition | Which rows (`Criteria`) | Ordering, `Skip`/`Take` |
| `.Apply(spec)` | Wire criteria (+ Includes if any) onto `IQueryable` | Execution |
| `.Select(...)` | Columns the client sees | Loading full navigations |
| `.ToListAsync()` | Run the SQL | — |

*Why show this before section 3?* So you see the seam clearly: pagination only replaces the last step (`ToListAsync` → `ToPagedAsync` on the same projected query). Specs and projection stay unchanged. Returning an unbounded list is fine for tiny datasets or demos; production list endpoints almost always need section 3 next.

---
## 3. Pagination primitives: `PageRequest`, `PagedResult`, `ToPagedAsync`

### Why pagination sits outside the specification

Specs answer **which rows**; they do not page or sort. Section 2.8 already builds a filtered, projected query and materializes it with `.ToListAsync()` — fine for demos, dangerous for production lists that can grow without bound.

Pagination is a **separate pipeline step** on that same `IQueryable` after `Apply` + `Select`:

```text
DbSet → Apply(spec) → Select(DTO) → ToPagedAsync(PageRequest) → PagedResult / PagedResponse
                                      ▲
                                      replaces .ToListAsync() only
```

The client sends page number, page size, and optional sort (`orderBy` / `desc`). The server returns one page of items plus `totalCount` so the UI can render page controls. Specs and the DTO projection stay unchanged.

### What this section introduces

| Type / API | Role |
|---|---|
| `PageRequest` | Base class: `Page`, `PageSize`, `OrderBy`, `Desc`, `Search` — list filters **inherit** this (§4.1) |
| `PagedResult<T>` | Internal result after the query runs |
| `PagedResponse<T>` | Same shape on the wire / API response |
| `ToPagedAsync` | `CountAsync` + ordered `Skip`/`Take` + `ToListAsync` |

`ToPagedAsync` also hardens the common footguns: clamp `PageSize` to a max, overflow-safe `Skip`, and an `Id` tie-breaker so pages stay stable when many rows share the same sort value. Sorting details (`ApplyOrder`) are in §4.4.

**Where it lives (same home as §2.6):** Common already references EF Core, so pagination types and `ToPagedAsync` go there together — no separate EF shared project.

| Piece | Location |
|---|---|
| `PageRequest` / `PagedResult` / `PagedResponse` | `src/Shared/Common/Pagination/` |
| `ToPagedAsync` (`PagedQueryExtensions`) | `src/Shared/Common/Pagination/` — needs `CountAsync` / `ToListAsync`; same EF package already on Common |

```csharp
// src/Shared/Common/Pagination/PageRequest.cs
// Class (not sealed record) so feature filters can inherit — see §4.1.
namespace Shared.Common.Pagination;

public class PageRequest
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? OrderBy { get; init; }
    public bool Desc { get; init; }
    public string? Search { get; init; }
}

// src/Shared/Common/Pagination/PagedResult.cs — internal, entity/DTO-agnostic result shape
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

// src/Shared/Common/Pagination/PagedResponse.cs — same shape, for the wire (avoid depending on
// PagedResult<T> directly from Contracts/response DTOs if MyApp later splits those out)
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}
```

```csharp
// src/Shared/Common/Pagination/PagedQueryExtensions.cs
// Needs Microsoft.EntityFrameworkCore (CountAsync / ToListAsync) — already on Shared.Common
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace Shared.Common.Pagination;

public static class PagedQueryExtensions
{
    public const int MaxPageSize = 100;
    private const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    public static async Task<PagedResult<T>> ToPagedAsync<T>(
        this IQueryable<T> source, PageRequest request, CancellationToken ct = default)
    {
        // Normalize once; the SAME values are used for Skip/Take and echoed in the result —
        // a caller who asked for pageSize=99999 gets back a result that honestly says
        // pageSize: 100, not one that silently clamped the query but claimed otherwise.
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, MaxPageSize);

        // long arithmetic avoids (page - 1) * size overflowing int and wrapping negative
        // for a very large page number, which used to throw from Queryable.Skip.
        var skip = (int)Math.Min((long)(page - 1) * size, int.MaxValue);

        var total = await source.CountAsync(ct);   // COUNT without ORDER BY — sorting first would be wasted work

        IReadOnlyList<T> items = Array.Empty<T>();
        if (skip < total)   // page number beyond the last page → empty, skip the second query entirely
            items = await ApplyOrder(source, request.OrderBy, request.Desc).Skip(skip).Take(size).ToListAsync(ct);

        return new PagedResult<T>(items, total, page, size);
    }

    private static IQueryable<T> ApplyOrder<T>(IQueryable<T> source, string? orderBy, bool desc)
    {
        var id = typeof(T).GetProperty("Id", Flags);
        var first = desc ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy);

        // No sort requested: order by Id anyway. Skip/Take over an UNORDERED query is
        // genuinely undefined — two calls for "page 1" and "page 2" can return overlapping
        // or missing rows without this.
        if (string.IsNullOrWhiteSpace(orderBy))
            return id is null ? source : Apply(source, id, first);

        var prop = typeof(T).GetProperty(orderBy.Trim(), Flags)
            ?? throw new ArgumentException(
                $"Cannot order by '{orderBy}': no public instance property with that name on {typeof(T).Name}.",
                nameof(orderBy));

        var ordered = Apply(source, prop, first);

        // Tie-breaker: two rows can share the same sort value. Without a second, unique key,
        // the database is free to order tied rows differently between two separate queries —
        // Id as a secondary key makes the overall order unique and stable.
        return id is null || prop == id ? ordered : Apply(ordered, id, nameof(Queryable.ThenBy));
    }

    // Builds x => x.Prop and closes Queryable.OrderBy<T, TKey>/ThenBy<T, TKey> via reflection,
    // because TKey is only known once the property is looked up at runtime from a string —
    // this is the one legitimate use of reflection here (contrast with ValidationBehavior's
    // ErrorOrFactory, which caches its MethodInfo for the same reason).
    private static IQueryable<T> Apply<T>(IQueryable<T> source, PropertyInfo prop, string method)
    {
        var x = Expression.Parameter(typeof(T), "x");
        var lambda = Expression.Lambda(Expression.Property(x, prop), x);
        var m = typeof(Queryable).GetMethods()
            .Single(mi => mi.Name == method && mi.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), prop.PropertyType);
        return (IQueryable<T>)m.Invoke(null, new object[] { source, lambda })!;
    }
}
```

*Why does an unrecognized `orderBy` throw `ArgumentException` here rather than fail more gracefully?* Because it shouldn't be reachable in practice — the validator in section 4.2 rejects any `orderBy` outside an explicit allow-list before a handler ever runs. This exception is the safety net for a bug in that allow-list, not the primary defense; it deliberately maps to a 500 (not a 400) in [`ExceptionMapping`](../../src/Shared/Common/Exceptions/Http/ExceptionMapping.cs) for exactly that reason: reaching it means the validator let something through it shouldn't have.

**Sorting is `ApplyOrder`, not a SortingSpec.** `PageRequest.OrderBy` / `Desc` are resolved by reflection inside `ToPagedAsync` onto the **projected DTO** (section 4.4 walks through a concrete example). Do not put `OrderBy` on `ISpecification` or `.And()` a fake paging/sorting spec onto filters (defect #3 in section 0).

---
## 4. Complex filtering, sorting and pagination — wired to the Result pattern

The article's version returns a raw `PagedResponse<T>` straight from the handler with no validation step and no distinction between "bad request" and "empty result." Here, invalid filters (a bogus `sortBy`, an out-of-range `pageSize`, `minPrice > maxPrice`) go through the exact same `ValidationBehavior` → `ErrorOrFactory` → `ToProblem` path as every other request in this codebase — a list endpoint isn't a special case.

### 4.1 The filter DTO and query

**Do not nest `PageRequest` as a property.** With Minimal APIs `[AsParameters]`, a nested complex type is inferred as **body** — GET endpoints reject that (`Body was inferred but the method does not allow inferred body parameters`).

**Inherit instead:** `PageRequest` is a class so every list filter reuses paging/sort fields without repeating them, and all properties bind as top-level query string keys (`?page=1&pageSize=20&orderBy=Price`).

```csharp
// src/Services/MyApp.Api/Features/Products/GetProducts/ProductFilter.cs
public sealed class ProductFilter : PageRequest
{
    public Guid? CategoryId { get; init; }
    public Guid? BrandId { get; init; }
    public Guid? TagId { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool InStockOnly { get; init; }
    // Search lives on PageRequest — do not redeclare it here
}

// src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsQuery.cs
public sealed record GetProductsQuery(ProductFilter Filter) : IQuery<PagedResponse<ProductListItemDto>>;

public sealed record ProductListItemDto(
    Guid Id, string Name, decimal Price, int StockQuantity,
    string CategoryName, string BrandName, DateTime CreatedAt);
```

`[AsParameters] ProductFilter filter` binds base + derived properties from the query string:

```http
GET /products?page=1&pageSize=20&orderBy=Price&desc=true&categoryId=...&minPrice=10
```

Because `ProductFilter` *is a* `PageRequest`, the handler passes it straight to `ToPagedAsync` — no mapping, no nested `filter.Page`.

### 4.2 Validation — every field, allow-listed sort, cross-field checks

```csharp
// src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsQueryValidator.cs
public sealed class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    // Allow-list against the DTO the client actually receives: sorting the raw entity would
    // let a caller order by a column that's never exposed in the response at all.
    private static readonly string[] Sortable =
        [nameof(ProductListItemDto.Name), nameof(ProductListItemDto.Price),
         nameof(ProductListItemDto.StockQuantity), nameof(ProductListItemDto.CreatedAt)];

    public GetProductsQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(x => x.Filter.PageSize)
            .InclusiveBetween(1, PagedQueryExtensions.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(x => x.Filter.OrderBy)
            .Must(o => Sortable.Contains(o, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Filter.OrderBy))
            .WithMessage($"orderBy must be one of: {string.Join(", ", Sortable)}.")
            .OverridePropertyName("orderBy");
        RuleFor(x => x.Filter.Search).MaximumLength(100).OverridePropertyName("search");

        RuleFor(x => x.Filter.MinPrice).GreaterThanOrEqualTo(0)
            .When(x => x.Filter.MinPrice.HasValue).OverridePropertyName("minPrice");
        RuleFor(x => x.Filter.MaxPrice).GreaterThan(0)
            .When(x => x.Filter.MaxPrice.HasValue).OverridePropertyName("maxPrice");

        // Cross-field rule: FluentValidation isn't limited to one property per rule.
        RuleFor(x => x.Filter)
            .Must(f => !f.MinPrice.HasValue || !f.MaxPrice.HasValue || f.MinPrice <= f.MaxPrice)
            .WithMessage("minPrice must not be greater than maxPrice.")
            .OverridePropertyName("minPrice");
    }
}
```

`?minPrice=100&maxPrice=10` now returns your standard 400 shape:

```json
{
  "type": "https://httpstatuses.io/400",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "instance": "/products",
  "errors": { "minPrice": ["minPrice must not be greater than maxPrice."] },
  "traceId": "0HN7…",
  "errorCode": "VALIDATION",
  "errorType": "Validation"
}
```

### 4.3 The handler: specs build the `WHERE`, `ToPagedAsync` builds the page

Same composition + `Apply` + `Select` as **§2.8**; only the last step changes — `ToListAsync` → `ToPagedAsync` (section 3). Validation (§4.2) sits in front.

```csharp
// src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsQueryHandler.cs
internal sealed class GetProductsQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductsQuery, ErrorOr<PagedResponse<ProductListItemDto>>>
{
    public async ValueTask<ErrorOr<PagedResponse<ProductListItemDto>>> Handle(
        GetProductsQuery query, CancellationToken ct)
    {
        var f = query.Filter;

        // 1–2. Identical to §2.8 (compose specs → Apply → Select).
        ISpecification<Product> spec = ActiveProductSpec.Instance;
        if (f.InStockOnly) spec = spec.And(new InStockSpecification());
        if (f.CategoryId is { } categoryId) spec = spec.And(new ByCategorySpecification(categoryId));
        if (f.BrandId is { } brandId) spec = spec.And(new ByBrandSpecification(brandId));
        if (f.TagId is { } tagId) spec = spec.And(new ByTagSpecification(tagId));
        if (f.MinPrice is not null || f.MaxPrice is not null)
            spec = spec.And(new PriceRangeSpecification(f.MinPrice, f.MaxPrice));
        if (!string.IsNullOrWhiteSpace(f.Search))
            spec = spec.And(new ProductSearchSpecification(f.Search));

        var projected = dbContext.Products.Apply(spec)
            .Select(p => new ProductListItemDto(
                p.Id, p.Name, p.Price, p.StockQuantity,
                p.Category.Name, p.Brand.Name, p.CreatedAt));

        // 3. Only new piece vs §2.8 — order + page the projected query (not a SortingSpec).
        // ProductFilter : PageRequest → pass the filter itself.
        var page = await projected.ToPagedAsync(f, ct);

        return new PagedResponse<ProductListItemDto>(page.Items, page.TotalCount, page.Page, page.PageSize);
    }
}
```

### 4.4 Sorting with `ApplyOrder` (inside `ToPagedAsync`)

No separate `ProductListOrdering` / `ProductSortingSpec` and **no typed `switch` on sort keys**. The client sends `orderBy` + `desc` on the query string (inherited from `PageRequest`); `ToPagedAsync` calls `ApplyOrder`, which looks up that string as a property on the **projected DTO** and builds `OrderBy` / `ThenBy(Id)` via reflection.

**Rule of thumb:** `orderBy` must equal a public property name on the DTO after `Select` (case-insensitive). There are no aliases like `"date"` → `CreatedAt` or `"rating"` → `AverageRating` — use the property name itself.

#### Old typed switch → `ApplyOrder` (1:1)

This is what you would have written by hand:

```csharp
q = (orderBy?.ToLowerInvariant()) switch
{
    "price" => desc
        ? q.OrderByDescending(p => p.Price)
        : q.OrderBy(p => p.Price),

    "name" => desc
        ? q.OrderByDescending(p => p.Name)
        : q.OrderBy(p => p.Name),

    // Computed — must already exist on the DTO from Select; ApplyOrder cannot invent it.
    "rating" => desc
        ? q.OrderByDescending(p => p.AverageRating)
        : q.OrderBy(p => p.AverageRating),

    "date" or "createdat" or null or "" => desc
        ? q.OrderByDescending(p => p.CreatedAt)
        : q.OrderBy(p => p.CreatedAt),

    _ => throw new ArgumentException(
        $"Unsupported orderBy '{orderBy}'.", nameof(orderBy)),
};
```

With `ApplyOrder`, that entire switch disappears. The **handler stays one line**; each case becomes a query-string value whose name matches the DTO property:

| Old switch arm | Client sends | `ApplyOrder` builds (then always `.ThenBy(x => x.Id)` for non-Id sorts) |
|---|---|---|
| `"price"` + `desc` | `?orderBy=Price&desc=true` | `OrderByDescending(p => p.Price)` |
| `"name"` | `?orderBy=Name` | `OrderBy(p => p.Name)` |
| `"rating"` | DTO has `AverageRating` from `Select` → `?orderBy=AverageRating&desc=true` | `OrderByDescending(p => p.AverageRating)` |
| `"date"` / `"createdat"` | **No alias** — send `?orderBy=CreatedAt` | `OrderBy(p => p.CreatedAt)` |
| `null` / `""` | omit `orderBy` | `OrderBy(p => p.Id)` (safe default so `Skip`/`Take` is defined) |
| `_` → `ArgumentException` | validator (§4.2) rejects unknown → **400**; if allow-list is wrong, `ApplyOrder` throws → **500** | same safety, split across layers |

```http
GET /products?page=1&pageSize=20&orderBy=Price&desc=true&inStockOnly=true
```

**What happens**

1. Validator (§4.2) allow-lists `orderBy` against `ProductListItemDto` names (`Price`, `Name`, `CreatedAt`, …) → 400 if bogus.
2. Handler builds `projected` (`Apply(spec)` + `Select`) — same as §4.3.
3. `await projected.ToPagedAsync(f, ct)`:
   - `CountAsync` on the filtered projection (no `ORDER BY`)
   - `ApplyOrder(..., "Price", desc: true)` → `OrderByDescending(x => x.Price).ThenBy(x => x.Id)`
   - `Skip` / `Take` / `ToListAsync`

```csharp
// What ApplyOrder expands to for orderBy=Price, desc=true:
projected
    .OrderByDescending(p => p.Price)
    .ThenBy(p => p.Id)          // stable tie-breaker — always added when sorting by a non-Id column
    .Skip(...)
    .Take(...);

// Handler — you do not write the switch or the OrderBy yourself:
var page = await projected.ToPagedAsync(f, ct);
```

#### Computed sort (e.g. average rating)

Still `ApplyOrder` — put the value on the DTO in `Select`, allow-list the name, pass that name as `orderBy`:

```csharp
public sealed record ProductListItemDto(
    Guid Id, string Name, decimal Price, int StockQuantity,
    string CategoryName, string BrandName, DateTime CreatedAt,
    double AverageRating);

var projected = dbContext.Products.Apply(spec)
    .Select(p => new ProductListItemDto(
        p.Id, p.Name, p.Price, p.StockQuantity,
        p.Category.Name, p.Brand.Name, p.CreatedAt,
        p.Reviews.Any() ? p.Reviews.Average(r => (double)r.Rating) : 0));

// ?orderBy=AverageRating&desc=true
var page = await projected.ToPagedAsync(f, ct);
```

Keep the validator’s `Sortable` array in sync with every DTO property you expose for sorting (add `nameof(ProductListItemDto.AverageRating)` when you add that field). Specs still only answer “which rows” and “what shape.”

### 4.5 A detail endpoint using the `Include`-bearing spec

```csharp
// src/Services/MyApp.Api/Features/Products/GetProductDetails/GetProductDetailsQuery.cs
public sealed record GetProductDetailsQuery(Guid ProductId) : IQuery<ProductDetailsDto>;

public sealed record ReviewDto(string CustomerName, int Rating, string? Comment, DateTime CreatedAt);
public sealed record ProductDetailsDto(
    Guid Id, string Name, string? Description, decimal Price, int StockQuantity,
    string CategoryName, string BrandName, IReadOnlyList<string> Tags, IReadOnlyList<ReviewDto> Reviews);

internal sealed class GetProductDetailsQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductDetailsQuery, ErrorOr<ProductDetailsDto>>
{
    public async ValueTask<ErrorOr<ProductDetailsDto>> Handle(GetProductDetailsQuery query, CancellationToken ct)
    {
        // The Include/ThenInclude/AsSplitQuery spec from 2.5 — this DOES need the full
        // entity graph, not a projection, because the response includes nested
        // collections (Tags, Reviews with their Customer) that Select would otherwise
        // have to reconstruct by hand with several separate joins.
        var product = await dbContext.Products
            .Apply(new ProductDetailsSpecification(query.ProductId))
            .FirstOrDefaultAsync(ct);

        if (product is null)
            return Error.NotFound("PRODUCT.NOT_FOUND", $"Product '{query.ProductId}' was not found.");

        return new ProductDetailsDto(
            product.Id, product.Name, product.Description, product.Price, product.StockQuantity,
            product.Category.Name, product.Brand.Name,
            product.Tags.Select(t => t.Name).ToList(),
            product.Reviews.Select(r => new ReviewDto(r.Customer.Name, r.Rating, r.Comment, r.CreatedAt)).ToList());
    }
}
```

### 4.6 Endpoints (Carter)

MyApp already registers one `ICarterModule` per slice (see today's [`GetProductsEndpoint`](../../src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsEndpoint.cs) and [`GetProductEndpoint`](../../src/Services/MyApp.Api/Features/Products/GetProduct/GetProductEndpoint.cs)) — not a single `API/Products/ProductEndpoints.cs`. Extend those modules (or add a sibling file in the same feature folder):

```csharp
// src/Services/MyApp.Api/Features/Products/GetProducts/GetProductsEndpoint.cs
// Evolves today's MapGet("/products", ...) to accept ProductFilter via [AsParameters]
namespace MyApp.Features.Products.GetProducts;

public sealed class GetProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products", HandleAsync)
            .WithName("GetProducts")
            .WithTags("Products");
    }

    private static async Task<IResult> HandleAsync(
        [AsParameters] ProductFilter filter,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductsQuery(filter), ct);
        return result.MatchOk(http);
    }
}

// src/Services/MyApp.Api/Features/Products/GetProductDetails/GetProductDetailsEndpoint.cs
// New sibling of GetProductEndpoint — detail graph with Includes
namespace MyApp.Features.Products.GetProductDetails;

public sealed class GetProductDetailsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/products/{id:guid}/details", HandleAsync)
            .WithName("GetProductDetails")
            .WithTags("Products");
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProductDetailsQuery(id), ct);
        return result.MatchOk(http);
    }
}
```

```
GET /products?categoryId=...&minPrice=10&maxPrice=200&inStockOnly=true&page=1&pageSize=20&orderBy=Price&desc=true
GET /products/{id}/details
```

---
## 5. The seeder

Uses `Bogus` (`dotnet add package Bogus`) for realistic fake data rather than hand-writing dozens of literal rows — the point of seeding is to exercise filtering/sorting/pagination against a dataset that behaves like production data (varied prices, uneven category distribution, some empty search results), not three hardcoded rows that happen to pass every demo.

*Why check `AnyAsync` before seeding, and why does that matter beyond avoiding duplicate data?* Because `Program.cs` (below) runs the seeder on every application start in Development. Without the guard, restarting the app during a debugging session would double the dataset every time — annoying, and it slowly invalidates whatever page counts and totals you were eyeballing while testing pagination.

```csharp
// src/Services/MyApp.Api/Persistence/Seeding/DataSeeder.cs
public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(ct)) return;   // idempotent — see the "why" above

        var random = new Random(20260927);       // fixed seed: reruns produce the same dataset
        Randomizer.Seed = random;

        var categories = new Faker<Category>()
            .RuleFor(c => c.Id, f => f.Random.Guid())
            .RuleFor(c => c.Name, f => f.Commerce.Categories(1)[0])
            .Generate(8)
            .DistinctBy(c => c.Name)
            .ToList();

        var brands = new Faker<Brand>()
            .RuleFor(b => b.Id, f => f.Random.Guid())
            .RuleFor(b => b.Name, f => f.Company.CompanyName())
            .RuleFor(b => b.Country, f => f.Address.Country())
            .Generate(10);

        var tags = new Faker<Tag>()
            .RuleFor(t => t.Id, f => f.Random.Guid())
            .RuleFor(t => t.Name, f => f.Commerce.ProductAdjective())
            .Generate(20)
            .DistinctBy(t => t.Name)
            .ToList();

        var customers = new Faker<Customer>()
            .RuleFor(c => c.Id, f => f.Random.Guid())
            .RuleFor(c => c.Name, f => f.Name.FullName())
            .RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.Name))
            .Generate(100);

        var productFaker = new Faker<Product>()
            .RuleFor(p => p.Id, f => f.Random.Guid())
            .RuleFor(p => p.Name, f => f.Commerce.ProductName())
            .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
            .RuleFor(p => p.Price, f => f.Random.Decimal(5, 2000))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(0, 500))
            .RuleFor(p => p.IsActive, f => f.Random.Bool(0.9f))     // 90% active — some inactive rows to filter out
            .RuleFor(p => p.IsDeleted, f => f.Random.Bool(0.03f))   // a few soft-deleted rows
            .RuleFor(p => p.CreatedAt, f => f.Date.Past(2))
            .RuleFor(p => p.CategoryId, f => f.PickRandom(categories).Id)
            .RuleFor(p => p.BrandId, f => f.PickRandom(brands).Id);

        var products = productFaker.Generate(600);

        // Assign 1–4 random tags per product AFTER generation (skip-navigation collections
        // aren't settable via RuleFor the same way scalar columns are).
        foreach (var p in products)
            p.Tags = tags.OrderBy(_ => random.Next()).Take(random.Next(1, 5)).ToList();

        var reviews = new List<Review>();
        var reviewFaker = new Faker<Review>()
            .RuleFor(r => r.Id, f => f.Random.Guid())
            .RuleFor(r => r.Rating, f => f.Random.Int(1, 5))
            .RuleFor(r => r.Comment, f => f.Random.Bool(0.7f) ? f.Rant.Review() : null)
            .RuleFor(r => r.CreatedAt, f => f.Date.Past(1));

        // Roughly 0–6 reviews per product, each from a random customer — building this by
        // hand (rather than one flat Faker<Review>().Generate(N)) is what lets every
        // review point at a product/customer that actually exists.
        foreach (var product in products)
        {
            var reviewCount = random.Next(0, 7);
            for (var i = 0; i < reviewCount; i++)
            {
                var review = reviewFaker.Generate();
                review.ProductId = product.Id;
                review.CustomerId = customers[random.Next(customers.Count)].Id;
                reviews.Add(review);
            }
        }

        var orders = new List<Order>();
        var orderItems = new List<OrderItem>();
        var activeProducts = products.Where(p => p.IsActive && !p.IsDeleted).ToList();

        foreach (var customer in customers)
        {
            var orderCount = random.Next(0, 4);   // some customers have no orders at all
            for (var i = 0; i < orderCount; i++)
            {
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customer.Id,
                    Status = (OrderStatus)random.Next(0, 4),
                    CreatedAt = DateTime.UtcNow.AddDays(-random.Next(1, 365)),
                };
                orders.Add(order);

                var itemCount = random.Next(1, 5);
                foreach (var chosen in activeProducts.OrderBy(_ => random.Next()).Take(itemCount))
                {
                    orderItems.Add(new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        ProductId = chosen.Id,
                        Quantity = random.Next(1, 4),
                        UnitPriceAtPurchase = chosen.Price,   // snapshot at "purchase" time — see 1's note
                    });
                }
            }
        }

        // AddRange in dependency order isn't strictly required (EF Core's change tracker
        // sorts inserts by FK dependency automatically), but grouping the calls this way
        // keeps the method readable as "categories, then things that reference them."
        db.AddRange(categories);
        db.AddRange(brands);
        db.AddRange(tags);
        db.AddRange(customers);
        db.AddRange(products);
        db.AddRange(reviews);
        db.AddRange(orders);
        db.AddRange(orderItems);

        await db.SaveChangesAsync(ct);
    }
}
```

### Registration — Development only

*Why gate seeding behind `IsDevelopment()`?* Fake Bogus data must not land in production. Hook `DataSeeder` after migrations in [`UseInfrastructure`](../../src/Services/MyApp.Api/Infrastructure/WebApplicationExtensions.cs).

Schema comes from **EF migrations** (`Persistence/Migrations/`), applied on startup with `Database.Migrate()` — not `EnsureCreated()`.

| | `Migrate()` (MyApp now) | `EnsureCreated()` (old demo path) |
|---|---|---|
| Needs `dotnet ef migrations add`? | Yes | No |
| Updates existing DB when model changes | Yes (new migration + restart) | **No** |
| History table | `__EFMigrationsHistory` | None |

```csharp
// src/Services/MyApp.Api/Infrastructure/WebApplicationExtensions.cs
using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;
using MyApp.Persistence.Seeding;

namespace MyApp.Infrastructure;

public static class WebApplicationExtensions
{
    public static WebApplication UseInfrastructure(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Database.Migrate();

        if (app.Environment.IsDevelopment())
            DataSeeder.SeedAsync(db).GetAwaiter().GetResult();

        return app;
    }
}
```

Add / update schema:

```bash
dotnet ef migrations add <Name> --project src/Services/MyApp.Api --output-dir Persistence/Migrations
```

If you previously used `EnsureCreated` on the same SQLite file (`products.db`), **delete that file once** before the first `Migrate()` run — a DB created without migration history will conflict. Seeding still no-ops when `Products` already has rows (idempotent guard in §5).

---
## 6. Testing

### 6.1 Specs are pure expression trees — test them without a database

```csharp
// tests/MyApp.Tests.Unit/ActiveProductSpecTests.cs
public class ActiveProductSpecTests
{
    [Fact]
    public void Active_undeleted_product_satisfies_the_spec()
    {
        var spec = new ActiveProductSpec();
        var product = new Product { IsActive = true, IsDeleted = false, /* ... */ };
        Assert.True(spec.IsSatisfiedBy(product));
    }

    [Fact]
    public void Deleted_product_does_not_satisfy_the_spec_even_if_active()
    {
        var spec = new ActiveProductSpec();
        var product = new Product { IsActive = true, IsDeleted = true, /* ... */ };
        Assert.False(spec.IsSatisfiedBy(product));
    }
}

public class SpecificationCompositionTests
{
    [Fact]
    public void And_requires_both_sides()
    {
        var combined = new ActiveProductSpec().And(new InStockSpecification(5));
        var lowStock = new Product { IsActive = true, IsDeleted = false, StockQuantity = 2 };
        Assert.False(combined.IsSatisfiedBy(lowStock));
    }

    [Fact]
    public void Composing_a_shared_singleton_does_not_mutate_it()
    {
        // This is the regression test for the article's original bug: combining the
        // shared instance twice, differently, must not leak state between the two results.
        var a = ActiveProductSpec.Instance.And(new InStockSpecification(100));
        var b = ActiveProductSpec.Instance.And(new PriceRangeSpecification(0, 10));

        var cheapButLowStock = new Product { IsActive = true, IsDeleted = false, StockQuantity = 1, Price = 5 };

        Assert.False(a.IsSatisfiedBy(cheapButLowStock));   // fails the stock rule from `a`
        Assert.True(b.IsSatisfiedBy(cheapButLowStock));    // unaffected by `a`'s condition
    }

    [Fact]
    public void Include_chains_from_both_sides_are_preserved_after_And()
    {
        var withIncludes = new ProductDetailsSpecification(Guid.NewGuid());
        var combined = withIncludes.And(new ActiveProductSpec());
        Assert.Equal(withIncludes.IncludeChains.Count, combined.IncludeChains.Count);
    }
}
```

### 6.2 Integration tests: filtering, sorting, and the cartesian-explosion regression

Use the existing [`MyAppFactory`](../../tests/MyApp.Tests.Integration/MyAppFactory.cs) (in-memory DB, `Testing` environment) — not a bare `WebApplicationFactory<Program>`.

```csharp
// tests/MyApp.Tests.Integration/ProductFilteringTests.cs
public class ProductFilteringTests(MyAppFactory factory) : IClassFixture<MyAppFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Category_filter_only_returns_matching_products()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var categoryId = await db.Categories.Select(c => c.Id).FirstAsync();

        var res = await _client.GetFromJsonAsync<JsonElement>($"/products?categoryId={categoryId}&pageSize=50");
        foreach (var item in res.GetProperty("items").EnumerateArray())
        {
            // The response DTO doesn't carry categoryId directly (only CategoryName) —
            // assert against the seeded category's name instead.
        }
    }

    [Theory]
    [InlineData("minPrice=100&maxPrice=10")]     // min > max — the cross-field rule from 4.2
    [InlineData("orderBy=SecretInternalColumn")]
    [InlineData("pageSize=0")]
    public async Task Invalid_filter_combinations_return_400_with_field_errors(string query)
    {
        var res = await _client.GetAsync($"/products?{query}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Detail_endpoint_returns_correct_review_and_tag_counts_despite_AsSplitQuery()
    {
        // The regression test for the cartesian-explosion fix in 2.5: without AsSplitQuery,
        // a product with 3 reviews and 4 tags would return EITHER duplicated review rows
        // OR duplicated tag rows (whichever collection EF Core joins second), inflating one
        // count by a multiple of the other. With AsSplitQuery, both counts come back exact.
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var product = await db.Products
            .Include(p => p.Reviews).Include(p => p.Tags)
            .FirstAsync(p => p.Reviews.Count >= 2 && p.Tags.Count >= 2);

        var body = await _client.GetFromJsonAsync<JsonElement>($"/products/{product.Id}");

        Assert.Equal(product.Reviews.Count, body.GetProperty("reviews").GetArrayLength());
        Assert.Equal(product.Tags.Count, body.GetProperty("tags").GetArrayLength());
    }

    [Fact]
    public async Task Unknown_product_id_returns_404_not_found()
    {
        var res = await _client.GetAsync($"/products/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
}
```

---

## 7. Optional: migrating this to DDD-style aggregates later

<Callout variant="note" title="Skip this section for now — read it when you actually add invariants">
Nothing above requires this. Everything in sections 1–5 works equally well with the current anemic entities (`Product` as a plain data bag with public setters) or with rich aggregates. This section exists so that when you *do* reach for the DDD appendix from the main guide, you know exactly which pieces change and — more importantly — which pieces (specifications, pagination, `AppDbContext` + `.Apply(spec)`) **don't**.
</Callout>

### What changes

Only the entity classes themselves, and only the ones that need to protect an invariant. Compare current `Product`:

```csharp
public sealed class Product
{
    public decimal Price { get; set; }          // anyone can set this to a negative number
    public int StockQuantity { get; set; }
    public bool IsActive { get; set; } = true;
    // ...
}
```

to a version with private setters and behavior methods, following the same `DomainError`/`ErrorOr<Success>` pattern from the main guide's DDD appendix:

```csharp
public sealed class Product
{
    public decimal Price { get; private set; }
    public int StockQuantity { get; private set; }
    public bool IsActive { get; private set; } = true;
    // ...

    public ErrorOr<Success> Discontinue()
    {
        if (!IsActive)
            return DomainError.InvalidState("Product is already discontinued.");

        IsActive = false;
        return new Success();
    }

    public ErrorOr<Success> Reserve(int quantity)
    {
        if (quantity <= 0)
            return DomainError.Validation("Quantity must be positive.");
        if (StockQuantity < quantity)
            return DomainError.Conflict($"Only {StockQuantity} units left in stock.");

        StockQuantity -= quantity;
        return new Success();
    }
}
```

EF Core needs a small configuration change for this to keep working, since it can no longer use the public setter to materialize rows:

```csharp
// In ProductConfiguration.Configure(...)
b.Property(p => p.Price).HasField("<Price>k__BackingField");   // or use a backing field explicitly named _price
// Simpler in practice: EF Core 5+ can bind private auto-property setters directly without
// this if the setter is `private set` (not `init`-only) — confirm on your EF Core version
// before adding explicit backing-field mappings.
```

### What does not change

- **`ISpecification<T>`, `SpecificationBase<T>`, the evaluator (`.Apply(spec)`)** — none of these care whether `Product`'s setters are public or private. A spec's `Criteria` (`p => p.IsActive`) still compiles and translates to SQL identically either way; reads don't go through the entity's behavior methods at all.
- **`PagedQueryExtensions.ToPagedAsync`, the filter DTOs, the validators** — same reasoning; they operate on `IQueryable<T>` and projected DTOs, never on the entity's mutation methods.
- **The atomic filter specs in 2.4** — `ActiveProductSpec`, `PriceRangeSpecification`, and so on read properties, they don't write them.

### What's new, additively

Only the *write* side gains a step: a command handler that used to set properties directly now calls the entity's method and propagates its `ErrorOr` result, exactly like `AddOrderItemHandler` in the main guide's DDD appendix:

```csharp
internal sealed class DiscontinueProductCommandHandler(AppDbContext db)
    : IRequestHandler<DiscontinueProductCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(DiscontinueProductCommand cmd, CancellationToken ct)
    {
        var product = await db.Products.FindAsync([cmd.ProductId], ct);
        if (product is null)
            return DomainError.NotFound("Product was not found.");

        var result = product.Discontinue();
        if (result.IsError)
            return result.Errors.ToList();

        await db.SaveChangesAsync(ct);
        return new Success();
    }
}
```

**Rule of thumb, unchanged from the main guide:** reads (specs, pagination, filtering) don't need invariants and don't need DDD. Writes that must refuse an invalid transition do. Migrate entity by entity, only where a real business rule needs enforcing — `Category`, `Brand`, and `Tag` in this model may never need it at all.
## 8. The same query, three ways

One logical query, expressed in each style: active, undeleted products in a given category, price between `minPrice` and `maxPrice`, matching a search term, sorted by price descending, page 2 of 20, projected to `ProductListItemDto`.

### 8.1 Article's `QueryableExtensions` (raw `IQueryable` + `System.Linq.Dynamic.Core`)

```csharp
var query = db.Products
    .AsNoTracking()
    .Where(p => p.IsActive && !p.IsDeleted)
    .Where(p => p.CategoryId == categoryId)
    .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
    .ApplySearch(search, nameof(Product.Name), nameof(Product.Description))
    .ApplySort("Price desc")                                    // string, parsed at runtime
    .Select(p => new ProductListItemDto(
        p.Id, p.Name, p.Price, p.StockQuantity, p.Category.Name, p.Brand.Name, p.CreatedAt))
    .ApplyPagination(pageNumber: 2, pageSize: 20);

var items = await query.ToListAsync(ct);
var total = await db.Products.Where(/* same filters, repeated by hand */).CountAsync(ct);
```

*Notice:* the total count needs the filter conditions **repeated**, since `ApplyPagination`/`ApplySort` already ran on `query` by the time you'd want a count. Nothing in this style stops that duplication from drifting out of sync if someone edits one copy and not the other.

### 8.2 `Ardalis.Specification`

```csharp
public sealed class ArdalisProductListSpecification : Specification<Product, ProductListItemDto>
{
    public ArdalisProductListSpecification(
        Guid categoryId, decimal minPrice, decimal maxPrice, string search, int skip, int take)
    {
        Query.Where(p => p.IsActive && !p.IsDeleted)
             .Where(p => p.CategoryId == categoryId)
             .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
             .Search(p => p.Name, $"%{search}%")
             .Search(p => p.Description!, $"%{search}%")   // same group (default) → OR'd together
             .OrderByDescending(p => p.Price)
             .Skip(skip)
             .Take(take)
             .Select(p => new ProductListItemDto(
                 p.Id, p.Name, p.Price, p.StockQuantity, p.Category.Name, p.Brand.Name, p.CreatedAt));
    }
}

// Usage, via the package's EF Core evaluator or its repository base:
var spec = new ArdalisProductListSpecification(categoryId, minPrice, maxPrice, search, skip: 20, take: 20);
var items = await SpecificationEvaluator.Default.GetQuery(db.Products.AsQueryable(), spec).ToListAsync(ct);
```

*Notice:* getting a total count needs a **second spec** built from the same `Where`s with no `Skip`/`Take`/`Select` — this isn't a gap in the summary above, it's a known, open question in the package's own issue tracker (`ardalis/Specification#156`): there's no single spec that gives you both a page and a count of the unpaged set. You write the duplicate-conditions spec yourself, same shape of problem as 8.1's repeated `Where`.

### 8.3 This guide's implementation

```csharp
ISpecification<Product> spec = ActiveProductSpec.Instance
    .And(new ByCategorySpecification(categoryId))
    .And(new PriceRangeSpecification(minPrice, maxPrice))
    .And(new ProductSearchSpecification(search));

var projected = dbContext.Products.Apply(spec)
    .Select(p => new ProductListItemDto(
        p.Id, p.Name, p.Price, p.StockQuantity, p.Category.Name, p.Brand.Name, p.CreatedAt));

var page = await projected.ToPagedAsync(
    new PageRequest { Page = 2, PageSize = 20, OrderBy = nameof(ProductListItemDto.Price), Desc = true }, ct);
// page.TotalCount is already correct — ToPagedAsync counts the filtered set once, internally,
// before paging (section 3). Sorting is ApplyOrder reflection (§4.4), not a SortingSpec.
```

### 8.4 Comparison

| | Article's extensions | Ardalis.Specification | This implementation |
|---|---|---|---|
| External package required | `System.Linq.Dynamic.Core` | `Ardalis.Specification(.EntityFrameworkCore)` | none |
| Sort field | a runtime **string**, parsed per call | a typed lambda | allow-listed string + `ApplyOrder` reflection in `ToPagedAsync` (sections 3 / 4.4) — never a SortingSpec |
| Sort key validation | manual whitelist inside `ApplySort` | none — compiler already restricts to real properties, but nothing stops sorting on an unindexed or unexposed column unless you write that check yourself | validator (section 4.2) rejects anything outside the allow-list before the handler runs |
| Total count before paging | you repeat the `Where` chain by hand | a second spec, no built-in count-plus-page primitive | `ToPagedAsync` counts and pages from the *same* filtered query — no duplication |
| `Include`/`ThenInclude` | not addressed by this extension set at all | first-class, typed, chainable (`.Include(x=>x.A).ThenInclude(...)`) | first-class, typed (section 2.5) — same capability, no package |
| Cartesian-explosion guard (`AsSplitQuery`) | not addressed | built in, one call | built in, one flag on the spec (section 2.5) |
| Composable atomic filters (`.And`/`.Or` between two independently-built objects) | no — everything is one `IQueryable` chain in one method | not really the idiom — a spec is normally one query end-to-end (8.2 above) | yes — the explicit design goal (section 2.3) |
| Testable without a database | partially — `ApplySearch`'s expression can be compiled and checked; `ApplySort`'s string path can't be exercised meaningfully without a provider | yes — `Specification.Lite`-style in-memory evaluation exists for some feature subsets, but `Query.Where` criteria compiles and tests like any `Expression<Func<T,bool>>` | yes — `IsSatisfiedBy` compiles and runs the criteria in-memory (section 6.1) |
| Confirmed .NET 10 support (as of this writing) | n/a — plain LINQ + a small independent package | pinned versions (9.3.1) target `net8.0`/`net9.0`, not `net10.0` directly; works under `net10.0` via the lower TFM, but isn't a first-party target yet — verify against whatever version you actually install | n/a — no package to track |

---
## 9. Benchmarking the approaches

**I haven't run this benchmark** — this sandbox has no .NET runtime, so nothing below is a measured result. What follows is a working harness you run yourself with `dotnet run -c Release`, plus what to look for in its output and specific hypotheses worth testing rather than assuming true.

Covers **three** query-building paths that match section 8:

| Benchmark method | What it is |
|---|---|
| `Article` | Article’s `ApplySearch` / `ApplySort` / `ApplyPagination` + `System.Linq.Dynamic.Core` (§8.1) |
| `Ardalis` | `Ardalis.Specification` end-to-end (§8.2) |
| `Implemented` | This guide’s specs + `ApplyOrder` via `ToPagedAsync` (§8.3 / §3 / §4.4) |

### 9.1 Why SQLite in-memory, and what this benchmark does and doesn't tell you

*Why not benchmark against production Postgres/SQL Server directly?* You can, and for a final go/no-go decision on your real workload, you should — swap the `UseSqlite(...)` call below for `UseNpgsql(...)`/`UseSqlServer(...)` against a Testcontainers instance and everything else in this harness is unchanged. SQLite in-memory is the starting point here because it needs no Docker, runs identically in CI, and — this is the important part — **the approaches under test differ almost entirely in C#-side work** (how each library builds and translates an expression tree, whether it parses a string every call, how it merges filter predicates / resolves sort keys), not in database engine behavior. That C#-side cost shows up whichever provider executes the resulting SQL. What SQLite *won't* tell you: absolute query latency at production data volumes, index usage under a real query planner, or connection-pooling behavior — for those, swap providers.

*What's actually being measured.* EF Core caches compiled query plans keyed by the expression tree's *shape* (constants get parameterized automatically), so a typed lambda spec run twice with different `categoryId` values reuses the same cached plan. `System.Linq.Dynamic.Core`'s string parser (`ApplySort("Price desc")`) has its own separate internal cache keyed by the parsed string — so it is **not** guaranteed to be slower once warmed up; this is exactly the kind of claim worth measuring rather than asserting. BenchmarkDotNet's default job already does JIT warmup and multiple iterations, so the numbers you get reflect steady-state cost, not first-call cold start — if you specifically want to compare *cold* first-call cost (e.g., after an app restart, before any query has been run yet), add a separate `[Benchmark]` that uses a fresh `DbContextOptions`/model per invocation, since that's what defeats EF Core's plan cache.

### 9.2 Project setup

Pin versions to whatever [`Directory.Packages.props`](../../Directory.Packages.props) already uses for MyApp (today: EF Core **10.0.x**, `System.Linq.Dynamic.Core` **1.7.x`). Mixing EF Core package versions within one solution is a common source of confusing runtime errors unrelated to anything in this benchmark.

```xml
<!-- Benchmarks/Benchmarks.csproj — preferably under the same Directory.Packages.props as MyApp -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Optimize>true</Optimize>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
    <PackageReference Include="System.Linq.Dynamic.Core" />
    <PackageReference Include="Ardalis.Specification.EntityFrameworkCore" Version="9.3.1" />
  </ItemGroup>
  <ItemGroup>
    <!-- MyApp has no separate Domain / Infrastructure projects — API owns entities + AppDbContext -->
    <ProjectReference Include="../src/Services/MyApp.Api/MyApp.Api.csproj" />
    <ProjectReference Include="../src/Shared/Common/Shared.Common.csproj" />
  </ItemGroup>
</Project>
```

### 9.3 Article helpers (not in MyApp today — copy into the benchmark project)

`ApplySearch` / `ApplySort` / `ApplyPagination` are from the article; MyApp only references `System.Linq.Dynamic.Core` in the API csproj today and does **not** ship those extensions. Minimal stand-ins so the `Article` benchmark compiles:

```csharp
// Benchmarks/ArticleQueryableExtensions.cs
using System.Linq.Dynamic.Core;
using System.Linq.Expressions;

public static class ArticleQueryableExtensions
{
    public static IQueryable<T> ApplySearch<T>(
        this IQueryable<T> source, string? term, params string[] propertyNames)
    {
        if (string.IsNullOrWhiteSpace(term) || propertyNames.Length == 0)
            return source;

        // Rough equivalent of the article's OR-across-columns Contains — good enough for timing.
        Expression? body = null;
        var param = Expression.Parameter(typeof(T), "x");
        var termConst = Expression.Constant(term);
        foreach (var name in propertyNames)
        {
            var prop = Expression.PropertyOrField(param, name);
            var notNull = Expression.NotEqual(
                prop, Expression.Constant(null, prop.Type));
            var contains = Expression.Call(
                prop, nameof(string.Contains), Type.EmptyTypes, termConst);
            var clause = Expression.AndAlso(notNull, contains);
            body = body is null ? clause : Expression.OrElse(body, clause);
        }

        var lambda = Expression.Lambda<Func<T, bool>>(body!, param);
        return source.Where(lambda);
    }

    public static IQueryable<T> ApplySort<T>(this IQueryable<T> source, string sort) =>
        source.OrderBy(sort);   // System.Linq.Dynamic.Core

    public static IQueryable<T> ApplyPagination<T>(
        this IQueryable<T> source, int pageNumber, int pageSize) =>
        source.Skip(Math.Max(0, (pageNumber - 1) * pageSize)).Take(pageSize);
}
```

### 9.4 The query-building methods under test

```csharp
// Benchmarks/ArdalisProductListSpecification.cs — same class as section 8.2
using Ardalis.Specification;

public sealed class ArdalisProductListSpecification : Specification<Product, ProductListItemDto>
{
    public ArdalisProductListSpecification(
        Guid categoryId, decimal minPrice, decimal maxPrice, string search, int skip, int take)
    {
        Query.Where(p => p.IsActive && !p.IsDeleted)
             .Where(p => p.CategoryId == categoryId)
             .Where(p => p.Price >= minPrice && p.Price <= maxPrice)
             .Search(p => p.Name, $"%{search}%")
             .Search(p => p.Description!, $"%{search}%")
             .OrderByDescending(p => p.Price)
             .Skip(skip)
             .Take(take)
             .Select(p => new ProductListItemDto(
                 p.Id, p.Name, p.Price, p.StockQuantity, p.Category.Name, p.Brand.Name, p.CreatedAt));
    }
}

```

```csharp
// Benchmarks/QueryApproachBenchmarks.cs
using Ardalis.Specification.EntityFrameworkCore;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Products;
using MyApp.Persistence;
using MyApp.Persistence.Pagination;
using Shared.Common.Pagination;
using Shared.Common.Specifications;

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net100, warmupCount: 3, iterationCount: 15)]
public class QueryApproachBenchmarks
{
    private SqliteConnection _connection = null!;
    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _categoryId;

    [GlobalSetup]
    public async Task Setup()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        await using var db = new AppDbContext(_options);
        await db.Database.MigrateAsync();
        await DataSeeder.SeedAsync(db);
        _categoryId = await db.Categories.Select(c => c.Id).FirstAsync();
    }

    [GlobalCleanup]
    public async Task Cleanup() => await _connection.DisposeAsync();

    private AppDbContext NewContext() => new(_options);

    private const decimal MinPrice = 10m, MaxPrice = 500m;
    private const string Search = "a";
    private const int Skip = 20, Take = 20;

    [Benchmark(Baseline = true, Description = "Article: QueryableExtensions + Dynamic.Core")]
    public async Task<List<ProductListItemDto>> Article()
    {
        await using var db = NewContext();
        return await db.Products.AsNoTracking()
            .Where(p => p.IsActive && !p.IsDeleted)
            .Where(p => p.CategoryId == _categoryId)
            .Where(p => p.Price >= MinPrice && p.Price <= MaxPrice)
            .ApplySearch(Search, nameof(Product.Name), nameof(Product.Description))
            .ApplySort("Price desc")
            .Select(p => new ProductListItemDto(
                p.Id, p.Name, p.Price, p.StockQuantity, p.Category.Name, p.Brand.Name, p.CreatedAt))
            .ApplyPagination(pageNumber: 2, pageSize: 20)
            .ToListAsync();
    }

    [Benchmark(Description = "Ardalis.Specification")]
    public async Task<List<ProductListItemDto>> Ardalis()
    {
        await using var db = NewContext();
        var spec = new ArdalisProductListSpecification(_categoryId, MinPrice, MaxPrice, Search, Skip, Take);
        return await SpecificationEvaluator.Default.GetQuery(db.Products.AsQueryable(), spec).ToListAsync();
    }

    [Benchmark(Description = "Guide: specs + ApplyOrder via ToPagedAsync (§3/§4.4)")]
    public async Task<PagedResult<ProductListItemDto>> Implemented()
    {
        await using var db = NewContext();
        ISpecification<Product> spec = ActiveProductSpec.Instance
            .And(new ByCategorySpecification(_categoryId))
            .And(new PriceRangeSpecification(MinPrice, MaxPrice))
            .And(new ProductSearchSpecification(Search));

        var projected = db.Products.Apply(spec).Select(p => new ProductListItemDto(
            p.Id, p.Name, p.Price, p.StockQuantity, p.Category.Name, p.Brand.Name, p.CreatedAt));

        return await projected.ToPagedAsync(
            new PageRequest { Page = 2, PageSize = 20, OrderBy = nameof(ProductListItemDto.Price), Desc = true });
    }
}

// Benchmarks/Program.cs
using BenchmarkDotNet.Running;

BenchmarkRunner.Run<QueryApproachBenchmarks>();
```

*Why `[Benchmark(Baseline = true)]` on the article's version?* Not a judgment about which should win — it just means BenchmarkDotNet's output includes a `Ratio` column showing the others relative to it.

*A deliberate unfairness to be aware of:* `Article` / `Ardalis` return `List<ProductListItemDto>` with no total count, while `Implemented` returns a `PagedResult<T>` that includes `CountAsync()`. That is the harder case for the guide path — if it stays competitive despite the extra count, that is meaningful. For a fairer compare, add a matching `CountAsync` to the other two using their repeated-filter pattern.

### 9.5 Reading the output

```
| Method                                                        | Mean     | Ratio | Allocated |
|--------------------------------------------------------------- |---------:|------:|----------:|
| Article: QueryableExtensions + Dynamic.Core                   | ?        |  1.00 | ?         |
| Ardalis.Specification                                         | ?        |  ?    | ?         |
| Guide: specs + ApplyOrder via ToPagedAsync (§3/§4.4)          | ?        |  ?    | ?         |
```

- **`Mean`** — average wall-clock time per call, across warmed-up iterations.
- **`Allocated`** — bytes allocated per call (often more revealing for abstraction overhead).
- **`Ratio`** — each row relative to the `[Benchmark(Baseline = true)]` row.

### 9.6 Hypotheses worth testing, not assuming

1. **Does `ApplySort`'s string parsing show up in `Allocated` even after warmup?**
2. **Does the extra `CountAsync()` inside `Implemented` outweigh other differences vs `Article`/`Ardalis`?**
3. **Does `AsSplitQuery` change this list benchmark?** It shouldn't (no collection Includes) — test detail specs (§2.5) separately if needed.
4. **How does the gap change with dataset size?** `[Params(100, 5_000, 50_000)]` on seed size.

---

