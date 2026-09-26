using Microsoft.EntityFrameworkCore;

namespace MyApp.Persistence;

/// <summary>
/// Demo Unit-of-Work wrapper. Optional in real projects — keep or drop per service needs.
/// On failure it rolls back and rethrows; no special concurrency type is introduced.
/// </summary>
public sealed class TransactionBehavior<TRequest, TResponse>(
    AppDbContext dbContext,
    ILogger<TransactionBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async ValueTask<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (typeof(TRequest).Name.EndsWith("Query", StringComparison.Ordinal))
            return await next(cancellationToken);

        // InMemory (tests) and some providers don't support user transactions.
        if (!dbContext.Database.IsRelational())
        {
            var response = await next(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            return response;
        }

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);
        try
        {
            var response = await next(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return response;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogWarning(ex, "Transaction rolled back for {Request}", typeof(TRequest).Name);
            throw;
        }
    }
}
