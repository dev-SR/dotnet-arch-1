using System.Diagnostics;
using Mediator;
using Shared.Common.Errors;

namespace Shared.Application.Behaviors;

/// <summary>
/// Marks the active handler span as an error when the handler returns a failed ErrorOr.
/// It does not change the HTTP status; the endpoint still decides the response.
/// </summary>
public sealed class ErrorTracingBehavior<TRequest, TResponse>
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var response = await next(cancellationToken);

        if (response is IErrorOr { IsError: true })
            Activity.Current?.SetStatus(ActivityStatusCode.Error, "ErrorOr failure");
        else
            Activity.Current?.SetStatus(ActivityStatusCode.Ok);

        return response;
    }
}
