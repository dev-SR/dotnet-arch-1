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
        return (IQueryable<T>)m.Invoke(null, [source, lambda])!;
    }
}
