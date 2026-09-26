using Microsoft.AspNetCore.Http;

namespace Shared.Common.Errors.Http;

public static class ErrorOrExtensions
{
    public static IResult ToResult<T>(this ErrorOr<T> r, HttpContext ctx, string? traceId = null)
        => !r.IsError ? TypedResults.Ok(r.Value) : r.Errors.ToProblem(ctx, traceId);

    private static IResult Match<T>(this ErrorOr<T> r, HttpContext ctx,
        Func<T, IResult> onSuccess, string? traceId = null)
        => r.Match(onValue: onSuccess, onError: errors => errors.ToProblem(ctx, traceId));

    public static IResult MatchOk<T>(this ErrorOr<T> r, HttpContext ctx, string? traceId = null)
        => r.Match(ctx, TypedResults.Ok, traceId);

    /// <summary>204 for void commands (<see cref="ICommand"/> → <c>ErrorOr&lt;Success&gt;</c>).</summary>
    public static IResult MatchNoContent(this ErrorOr<Success> r, HttpContext ctx, string? traceId = null)
        => r.Match(ctx, _ => TypedResults.NoContent(), traceId);

    public static IResult MatchCreated<T>(this ErrorOr<T> r, HttpContext ctx,
        Func<T, string> locationSelector, string? traceId = null)
        => r.Match(ctx, v => TypedResults.Created(locationSelector(v), v), traceId);
}
