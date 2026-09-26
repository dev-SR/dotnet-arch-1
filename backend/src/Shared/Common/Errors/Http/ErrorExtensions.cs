using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Shared.Common.Errors.Http;

public static class ErrorExtensions
{
    private const string ProblemJson = "application/problem+json";

    // One Error → one ProblemDetails body. Used when a failure is a single, specific
    // thing (not found, forbidden, conflict, an unexpected error).
    public static IResult ToProblem(this Error error, HttpContext ctx, string? traceId = null)
    {
        var status = (int)MapStatusCode(error);
        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),   // "Not Found", "Conflict", ... — from ASP.NET Core, not hand-typed
            // Never echo the internal description on 5xx: see the "why" below.
            Detail = status >= 500 ? "An unexpected error occurred." : error.Description,
            Type = $"https://httpstatuses.io/{status}",
            Instance = ctx.Request.Path,
        };
        Decorate(problem, ctx, traceId, error.Code, error.Type.ToString());

        // TypedResults (not Results): the non-generic Results.Json(...) returns the
        // interface IResult with no compile-time guarantee of *what* it writes; TypedResults
        // gives you a concrete result type, which is easier to unit-test (see section 9.3)
        // and documents itself better in OpenAPI generation.
        return TypedResults.Json(problem, statusCode: status, contentType: ProblemJson);
    }

    // A LIST of errors → one ProblemDetails body. Needed because a single request can
    // fail several validation rules at once (section 4), and the caller should see all
    // of them in one round trip, not fix one field, resubmit, and hit the next.
    public static IResult ToProblem(this IReadOnlyList<Error> errors, HttpContext ctx, string? traceId = null)
    {
        if (errors.Count == 0)
            // Defensive: an ErrorOr in the IsError state with zero errors shouldn't be
            // reachable, but if it happens, fail loudly as a 500 rather than silently as a 200.
            return Error.Unexpected("EMPTY_ERROR_LIST", "An ErrorOr failure carried no errors.")
                .ToProblem(ctx, traceId);

        // If the list mixes validation errors with something else (say a validator ran
        // AND a Forbidden check failed), the non-validation error wins: a caller who isn't
        // allowed to do this at all shouldn't be told their field formatting first.
        var nonValidation = errors.FirstOrDefault(e => e.Type != ErrorType.Validation);
        if (!nonValidation.Equals(default(Error))) return nonValidation.ToProblem(ctx, traceId);

        // All validation: group by field (Code) so the client gets { "FirstName": [...], "Email": [...] }
        // instead of a flat, unstructured list it would have to parse itself.
        var map = errors.GroupBy(e => e.Code)
                        .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

        var problem = new ValidationProblemDetails(map)
        {
            Status = 400,
            Title = "One or more validation errors occurred.",
            Type = "https://httpstatuses.io/400",
            Instance = ctx.Request.Path,
        };
        Decorate(problem, ctx, traceId, "VALIDATION", nameof(ErrorType.Validation));
        return TypedResults.Json(problem, statusCode: 400, contentType: ProblemJson);
    }

    private static void Decorate(ProblemDetails p, HttpContext ctx, string? traceId, string code, string type)
    {
        // HttpContext.TraceIdentifier is a per-request id ASP.NET Core generates automatically
        // (and, if you've wired up W3C trace context / OpenTelemetry, ties back to a distributed
        // trace). Putting it in every error body means "what happened to my request?" can always
        // be answered by grepping logs for this one string — this is the single most useful field
        // for a support engineer looking at a bug report.
        p.Extensions["traceId"] = traceId ?? ctx.TraceIdentifier;
        p.Extensions["errorCode"] = code;     // stable machine-readable string a client can switch() on
        p.Extensions["errorType"] = type;     // the ErrorType name, mostly useful for debugging/logging
    }

    // The single place ErrorType decides an HTTP status. Anyone reviewing "what does a
    // Conflict error return?" reads this one switch, not twelve scattered ternaries.
    public static HttpStatusCode MapStatusCode(Error error) => error.Type switch
    {
        ErrorType.Failure             => HttpStatusCode.BadRequest,
        ErrorType.Validation          => HttpStatusCode.BadRequest,
        ErrorType.Unauthorized        => HttpStatusCode.Unauthorized,
        ErrorType.Forbidden           => HttpStatusCode.Forbidden,
        ErrorType.NotFound            => HttpStatusCode.NotFound,
        ErrorType.Conflict            => HttpStatusCode.Conflict,
        ErrorType.ServiceUnavailable  => HttpStatusCode.ServiceUnavailable,
        ErrorType.Unexpected          => HttpStatusCode.InternalServerError,
        _                             => HttpStatusCode.InternalServerError,   // unreachable but exhaustive
    };
}
