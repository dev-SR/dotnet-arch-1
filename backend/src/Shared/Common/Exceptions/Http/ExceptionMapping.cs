using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace Shared.Common.Exceptions.Http;

public static class ExceptionMapping
{
    public static (int Status, string Code) Map(Exception ex) => ex switch
    {
        BadHttpRequestException e => (e.StatusCode, "BAD_REQUEST"),
        DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "CONCURRENCY_CONFLICT"),
        OperationCanceledException => (499, "REQUEST_ABORTED"),
        _ => (StatusCodes.Status500InternalServerError, "INTERNAL_ERROR"),
    };

    /// <summary>
    /// When true, .NET 10 <c>UseExceptionHandler</c> suppresses UnhandledException diagnostics.
    /// </summary>
    public static bool IsExpected(Exception ex)
        => ex is OperationCanceledException
           || Map(ex).Status is >= 400 and < 500;
}
