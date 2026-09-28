using System.Linq.Expressions;

namespace Shared.Common.Specifications;

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
