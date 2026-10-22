using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Application.Persistence;
using Wolverine.EntityFrameworkCore;

namespace MyApp.Messaging;

/// <summary>
/// After <see cref="DbContext.SaveChangesAsync"/>, flushes enlisted Wolverine outbox messages.
/// </summary>
public sealed class WolverineDbContextCommitFlush(IServiceProvider services) : IDbContextCommitFlush
{
    public async ValueTask AfterSaveChangesAsync(DbContext dbContext, CancellationToken cancellationToken = default)
    {
        var outbox = services.GetService<IDbContextOutbox>();
        if (outbox is null)
            return;

        if (outbox.ActiveContext is null || !ReferenceEquals(outbox.ActiveContext, dbContext))
            outbox.Enroll(dbContext);

        await outbox.FlushOutgoingMessagesAsync();
    }
}
