using FluentValidation;
using Mediator;
using Microsoft.Extensions.Logging;
using Shared.Common.Errors;

namespace Shared.Application.Behaviors;

public sealed class ValidationBehavior<TRequest, TResponse>(
    IEnumerable<IValidator<TRequest>> validators,
    ILogger<ValidationBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var list = validators as IValidator<TRequest>[] ?? validators.ToArray();
        if (list.Length == 0)
            return await next(cancellationToken);

        var failures = (await Task.WhenAll(list.Select(v => v.ValidateAsync(request, cancellationToken))))
            .SelectMany(r => r.Errors)
            .ToList();

        if (failures.Count == 0)
            return await next(cancellationToken);

        logger.LogWarning(
            "Validation failed for {Request}: {Count} error(s)",
            typeof(TRequest).Name,
            failures.Count);

        var errors = failures
            .Select(f => Error.ValidationField(f.PropertyName, f.ErrorMessage))
            .ToList();

        // TResponse is ErrorOr<T> at runtime; open-generic DI can't express that in the signature.
        return ErrorOrFactory.Failure<TResponse>(errors);
    }
}
