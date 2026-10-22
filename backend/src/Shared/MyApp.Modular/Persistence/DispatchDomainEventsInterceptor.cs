using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shared.Domain;

namespace MyApp.Modular.Persistence;

/// <summary>
/// Publishes domain events <b>after</b> a successful save so failed transactions do not notify.
/// </summary>
public sealed class DispatchDomainEventsInterceptor(IPublisher publisher) : SaveChangesInterceptor
{
    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        DispatchDomainEvents(eventData.Context).GetAwaiter().GetResult();
        return base.SavedChanges(eventData, result);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        await DispatchDomainEvents(eventData.Context, cancellationToken).ConfigureAwait(false);
        return await base.SavedChangesAsync(eventData, result, cancellationToken).ConfigureAwait(false);
    }

    private async Task DispatchDomainEvents(
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

        var domainEvents = aggregates
            .SelectMany(a => a.ClearDomainEvents())
            .ToList();

        foreach (var domainEvent in domainEvents)
            await publisher.Publish(domainEvent, cancellationToken).ConfigureAwait(false);
    }
}
