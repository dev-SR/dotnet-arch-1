// src/Shared/Common/Specifications/SpecificationBase.cs

using System.Linq.Expressions;

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
