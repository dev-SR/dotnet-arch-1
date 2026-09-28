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
