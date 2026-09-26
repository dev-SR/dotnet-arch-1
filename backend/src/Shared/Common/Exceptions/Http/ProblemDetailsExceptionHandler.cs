using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Shared.Common.Exceptions.Http;

/// <summary>
/// ASP.NET Core <see cref="IExceptionHandler"/> for the exception channel.
/// Converts unhandled exceptions into RFC 9457 Problem Details (same shape as ErrorOr → ToProblem).
/// </summary>
public sealed class ProblemDetailsExceptionHandler(
    IProblemDetailsService problems,
    ILogger<ProblemDetailsExceptionHandler> log,
    IHostEnvironment env) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx,
        Exception ex,
        CancellationToken ct)
    {
        if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
            return true;

        var (status, code) = ExceptionMapping.Map(ex);
        if (status < 500)
            log.LogWarning(ex, "Handled {Code} on {Path}", code, ctx.Request.Path);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = ReasonPhrases.GetReasonPhrase(status),
            Type = $"https://httpstatuses.io/{status}",
            Instance = ctx.Request.Path,
            Detail = status >= 500 && !env.IsDevelopment()
                ? "An unexpected error occurred."
                : ex.Message,
        };
        problem.Extensions["traceId"] = ctx.TraceIdentifier;
        problem.Extensions["errorCode"] = code;

        ctx.Response.StatusCode = status;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = ctx,
            Exception = ex,
            ProblemDetails = problem,
        });
    }
}
