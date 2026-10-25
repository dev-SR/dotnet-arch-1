using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Shared.Application.Abstractions.Commands;
using Shared.Application.Persistence;
using Shared.Common.Errors;

namespace Shared.Application.Behaviors;

/// <summary>
/// Commits changes on whichever <see cref="DbContext"/>(s) the handler touched.
/// Hosts register contexts via <c>AddTransactionalDbContext&lt;T&gt;</c>.
/// Optional <see cref="IDbContextCommitFlush"/> flushes messaging outboxes after save.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    IEnumerable<DbContext> dbContexts,
    IEnumerable<IDbContextCommitFlush> commitFlushes,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly bool IsCommand = typeof(ICommandMarker).IsAssignableFrom(typeof(TRequest));
    private static readonly bool PersistOnFailure = typeof(IPersistOnFailure).IsAssignableFrom(typeof(TRequest));

    public async ValueTask<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var contexts = dbContexts as DbContext[] ?? dbContexts.ToArray();
        var flushes = commitFlushes as IDbContextCommitFlush[] ?? commitFlushes.ToArray();
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

            var saved = new List<DbContext>();
            foreach (var db in contexts.Where(c => c.ChangeTracker.HasChanges()))
            {
                // Wolverine IDbContextOutbox.Enroll (e.g. during SavingChanges) may open a
                // transaction mid-SaveChanges — never nest BeginTransaction; always Commit
                // after SaveChanges (same order as DbContextOutbox.SaveChangesAndFlushMessagesAsync)
                // or the unit of work rolls back on dispose while the API already returned success.
                if (db.Database.CurrentTransaction is not null)
                {
                    await db.SaveChangesAsync(ct);
                    await CommitIfPresentAsync(db, ct);
                    saved.Add(db);
                    continue;
                }

                if (flushes.Length > 0 || !db.Database.IsRelational())
                {
                    await db.SaveChangesAsync(ct);
                    await CommitIfPresentAsync(db, ct);
                    saved.Add(db);
                    continue;
                }

                var strategy = db.Database.CreateExecutionStrategy();
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await db.Database.BeginTransactionAsync(ct);
                    await db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                });
                saved.Add(db);
            }

            foreach (var db in saved)
                await FlushAsync(db, flushes, ct);

            return response;
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            logger.LogWarning(ex, "Unique constraint hit in {Request}", typeof(TRequest).Name);
            return ErrorOrFactory.Failure<TResponse>(
                [Error.Conflict("DB.CONFLICT", "The resource already exists or was changed by another request.")]);
        }
    }

    private static async Task CommitIfPresentAsync(DbContext db, CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is not null)
            await db.Database.CurrentTransaction.CommitAsync(ct);
    }

    private static async Task FlushAsync(
        DbContext db, IReadOnlyList<IDbContextCommitFlush> flushes, CancellationToken ct)
    {
        foreach (var flush in flushes)
            await flush.AfterSaveChangesAsync(db, ct);
    }

    private static bool ShouldCommit(TResponse response) =>
        PersistOnFailure || response is not IErrorOr { IsError: true };
}
