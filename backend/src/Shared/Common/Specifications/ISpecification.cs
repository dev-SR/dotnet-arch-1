using System.Linq.Expressions;

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
