using System.Linq.Expressions;

namespace Shared.Common.Specifications;

public static class SpecificationExtensions
{
    extension<T>(ISpecification<T> left) where T : class
    {
        public ISpecification<T> And(ISpecification<T> right) => new CombinedSpecification<T>(left, right, CombineMode.And);
        public ISpecification<T> Or(ISpecification<T> right) => new CombinedSpecification<T>(left, right, CombineMode.Or);
        public ISpecification<T> Not() => new NegatedSpecification<T>(left);
    }
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
        var (l, r) => mode == CombineMode.And ? l!.And(r!) : l!.Or(r!),
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
