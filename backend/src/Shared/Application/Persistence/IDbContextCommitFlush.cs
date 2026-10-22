using Microsoft.EntityFrameworkCore;

namespace Shared.Application.Persistence;

/// <summary>
/// Optional post-SaveChanges hook (e.g. Wolverine outbox flush).
/// No-op when messaging is not registered.
/// </summary>
public interface IDbContextCommitFlush
{
    ValueTask AfterSaveChangesAsync(DbContext dbContext, CancellationToken cancellationToken = default);
}
