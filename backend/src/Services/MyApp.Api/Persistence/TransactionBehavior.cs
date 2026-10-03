using Microsoft.EntityFrameworkCore;
using Shared.Application.Abstractions.Commands;
using Shared.Common.Errors;

namespace MyApp.Persistence;

public sealed class TransactionBehavior<TRequest, TResponse>(
    AppDbContext db,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly bool IsCommand = typeof(ICommandMarker).IsAssignableFrom(typeof(TRequest));
    private static readonly bool PersistOnFailure = typeof(IPersistOnFailure).IsAssignableFrom(typeof(TRequest));

    public async ValueTask<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!IsCommand || db.Database.CurrentTransaction is not null)
            return await next(ct);

        try
        {
            if (!db.Database.IsRelational())
            {
                var r = await next(ct);
                if (ShouldCommit(r)) await db.SaveChangesAsync(ct);
                return r;
            }

            var strategy = db.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                db.ChangeTracker.Clear();
                await using var tx = await db.Database.BeginTransactionAsync(ct);

                var response = await next(ct);

                if (!ShouldCommit(response))
                {
                    await tx.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    return response;
                }

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                return response;
            });
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            logger.LogWarning(ex, "Unique constraint hit in {Request}", typeof(TRequest).Name);
            return ErrorOrFactory.Failure<TResponse>(
                [Error.Conflict("DB.CONFLICT", "The resource already exists or was changed by another request.")]);
        }
    }

    private static bool ShouldCommit(TResponse response) =>
        PersistOnFailure || response is not IErrorOr { IsError: true };
}
