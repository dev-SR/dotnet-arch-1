using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Catalog.Persistence;
using MyApp.Modules.Orders.Persistence;
using MyApp.Modules.Users.Persistence;
using Shared.Application.Abstractions.Commands;
using Shared.Common.Errors;

namespace MyApp.ApiHost.Persistence;

/// <summary>
/// Commits changes on whichever module DbContext(s) the handler touched.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    UsersDbContext users,
    CatalogDbContext catalog,
    OrdersDbContext orders,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly bool IsCommand = typeof(ICommandMarker).IsAssignableFrom(typeof(TRequest));
    private static readonly bool PersistOnFailure = typeof(IPersistOnFailure).IsAssignableFrom(typeof(TRequest));

    public async ValueTask<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var contexts = new DbContext[] { users, catalog, orders };
        if (!IsCommand || contexts.Any(c => c.Database.CurrentTransaction is not null))
            return await next(ct);

        try
        {
            var response = await next(ct);
            if (!ShouldCommit(response))
            {
                foreach (var db in contexts)
                    db.ChangeTracker.Clear();
                return response;
            }

            foreach (var db in contexts.Where(c => c.ChangeTracker.HasChanges()))
            {
                if (!db.Database.IsRelational())
                {
                    await db.SaveChangesAsync(ct);
                    continue;
                }

                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                });
            }

            return response;
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
