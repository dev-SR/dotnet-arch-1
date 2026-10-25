using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Domain;
using Wolverine.EntityFrameworkCore;

namespace MyApp.Messaging.Persistence;

/// <summary>
/// Generic domain-event dispatch hook on <b>SavingChanges</b> (before the business rows persist).
///
/// Roles:
///  - this interceptor: <i>when + where</i> — enroll Wolverine's EF outbox on the saving
///    <see cref="DbContext"/>, drain a snapshot of every pending domain event, then hand that
///    snapshot to Mediator. Knows nothing about concrete event types.
///  - <see cref="INotificationHandler{TNotification}"/> implementations: <i>what</i> — in-process
///    reactions (logging) and the domain→integration bridge (outbox.PublishAsync of the matching
///    integration event). Handlers must NOT call SaveChanges.
///
/// Dispatch policy: only events that existed when the save began are published. Events raised by
/// handlers during dispatch are ignored for this save (they stay on the aggregate until a later
/// SaveChanges, or are discarded with the scoped DbContext on failure).
///
/// Sync path: <see cref="SavingChanges"/> throws when domain events are pending — callers must
/// use <see cref="DbContext.SaveChangesAsync"/>. Blocking async dispatch with GetAwaiter().GetResult()
/// is deliberately rejected.
///
/// Because Wolverine tracks outbox envelopes on the same enrolled context and the flush only runs
/// after <c>TransactionBehavior</c> commits, a failed save publishes nothing: the unit of work
/// rolls back together with its outbox rows. Failed units of work must be discarded (scoped
/// DbContext per request) — do not reuse a context after a failed save with drained events.
/// </summary>
public sealed class TransactionalDomainEventInterceptor(IDbContextOutbox outbox, IPublisher publisher)
    : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (HasPendingDomainEvents(eventData.Context))
        {
            throw new InvalidOperationException(
                "Domain events require SaveChangesAsync. Synchronous SaveChanges() is not supported " +
                "when aggregates have pending domain events — async Mediator handlers must not be " +
                "blocked with GetAwaiter().GetResult().");
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await PublishDomainEventsAsync(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavingChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private static bool HasPendingDomainEvents(DbContext? context) =>
        context is not null
        && context.ChangeTracker
            .Entries<IAggregate>()
            .Any(a => a.Entity.DomainEvents.Count > 0);

    private async Task PublishDomainEventsAsync(
        DbContext? context,
        CancellationToken cancellationToken = default)
    {
        if (context is null)
            return;

        var aggregates = context.ChangeTracker
            .Entries<IAggregate>()
            .Where(a => a.Entity.DomainEvents.Count > 0)
            .Select(a => a.Entity)
            .ToList();

        if (aggregates.Count == 0)
            return;

        // Drain EVERY aggregate before any Publish — a consistent snapshot of "events that
        // existed when the save began". Handlers that raise new events during dispatch do not
        // affect this pass (order-independent; no cascade into already-processed aggregates).
        var pending = new List<IDomainEvent>();
        foreach (var aggregate in aggregates)
            pending.AddRange(aggregate.ClearDomainEvents());

        // Bind the Wolverine outbox to this context BEFORE dispatch so notification handlers
        // can PublishAsync integration events transactionally.
        outbox.Enroll(context);

        foreach (var domainEvent in pending)
            await publisher.Publish(domainEvent, cancellationToken).ConfigureAwait(false);
    }
}
