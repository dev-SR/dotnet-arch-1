using FluentAssertions;
using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.Messaging.Persistence;
using NSubstitute;
using Shared.Domain;
using Wolverine.EntityFrameworkCore;

namespace MyApp.Tests.Unit;

file sealed record TestDomainEvent(Guid Id, string Label = "") : DomainEvent;

internal sealed class TestAggregate : Aggregate
{
    public string Name { get; set; } = "test";
}

internal sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();
}

internal sealed class RecordingPublisher : IPublisher
{
    public List<INotification> Published { get; } = [];
    public Action<INotification>? OnPublish { get; set; }

    public ValueTask Publish(
        INotification notification,
        CancellationToken cancellationToken = default)
    {
        Published.Add(notification);
        OnPublish?.Invoke(notification);
        return ValueTask.CompletedTask;
    }
}

public class TransactionalDomainEventInterceptorTests
{
    private static TestDbContext CreateContext(TransactionalDomainEventInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new TestDbContext(options);
    }

    [Fact]
    public async Task SavingChangesAsync_DispatchesAllDomainEvents_AndClearsThem()
    {
        var outbox = Substitute.For<IDbContextOutbox>();
        var publisher = new RecordingPublisher();

        var db = CreateContext(new TransactionalDomainEventInterceptor(outbox, publisher));
        var aggregate = new TestAggregate();
        var first = new TestDomainEvent(Guid.NewGuid(), "a");
        var second = new TestDomainEvent(Guid.NewGuid(), "b");
        aggregate.AddDomainEvent(first);
        aggregate.AddDomainEvent(second);
        db.Aggregates.Add(aggregate);

        await db.SaveChangesAsync();

        publisher.Published.Should().BeEquivalentTo(new INotification[] { first, second },
            options => options.WithStrictOrdering());
        aggregate.DomainEvents.Should().BeEmpty();
        outbox.Received(1).Enroll(Arg.Is<DbContext>(c => ReferenceEquals(c, db)));
    }

    [Fact]
    public async Task SavingChangesAsync_WithoutDomainEvents_DoesNotEnrollOutbox()
    {
        var outbox = Substitute.For<IDbContextOutbox>();
        var publisher = new RecordingPublisher();

        var db = CreateContext(new TransactionalDomainEventInterceptor(outbox, publisher));
        db.Aggregates.Add(new TestAggregate());

        await db.SaveChangesAsync();

        outbox.DidNotReceive().Enroll(Arg.Any<DbContext>());
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingChangesAsync_DrainsSnapshotBeforeDispatch_IgnoresEventsRaisedDuringPublish()
    {
        var outbox = Substitute.For<IDbContextOutbox>();
        var publisher = new RecordingPublisher();

        var db = CreateContext(new TransactionalDomainEventInterceptor(outbox, publisher));
        var first = new TestAggregate { Name = "first" };
        var second = new TestAggregate { Name = "second" };
        var e1 = new TestDomainEvent(Guid.NewGuid(), "from-first");
        var e2 = new TestDomainEvent(Guid.NewGuid(), "from-second");
        var cascade = new TestDomainEvent(Guid.NewGuid(), "raised-during-dispatch");
        first.AddDomainEvent(e1);
        second.AddDomainEvent(e2);
        db.Aggregates.AddRange(first, second);

        // Mid-dispatch: raise a new event on the already-drained first aggregate.
        // Snapshot policy: must NOT appear in this save's published set.
        publisher.OnPublish = _ =>
        {
            if (first.DomainEvents.Count == 0)
                first.AddDomainEvent(cascade);
        };

        await db.SaveChangesAsync();

        publisher.Published.Should().BeEquivalentTo(new INotification[] { e1, e2 });
        publisher.Published.Should().NotContain(cascade);
        // Cascade remains on the aggregate (drained snapshot only; not this pass).
        first.DomainEvents.Should().ContainSingle().Which.Should().Be(cascade);
    }

    [Fact]
    public void SavingChanges_Sync_WithPendingDomainEvents_Throws()
    {
        var outbox = Substitute.For<IDbContextOutbox>();
        var publisher = new RecordingPublisher();

        var db = CreateContext(new TransactionalDomainEventInterceptor(outbox, publisher));
        var aggregate = new TestAggregate();
        aggregate.AddDomainEvent(new TestDomainEvent(Guid.NewGuid()));
        db.Aggregates.Add(aggregate);

        var act = () => db.SaveChanges();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SaveChangesAsync*");
        publisher.Published.Should().BeEmpty();
        outbox.DidNotReceive().Enroll(Arg.Any<DbContext>());
    }

    [Fact]
    public void SavingChanges_Sync_WithoutDomainEvents_Succeeds()
    {
        var outbox = Substitute.For<IDbContextOutbox>();
        var publisher = new RecordingPublisher();

        var db = CreateContext(new TransactionalDomainEventInterceptor(outbox, publisher));
        db.Aggregates.Add(new TestAggregate());

        var count = db.SaveChanges();

        count.Should().Be(1);
        publisher.Published.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingChangesAsync_WhenPublisherThrows_Propagates_AndSaveDoesNotComplete()
    {
        var outbox = Substitute.For<IDbContextOutbox>();
        var publisher = new RecordingPublisher
        {
            OnPublish = _ => throw new InvalidOperationException("handler failed"),
        };

        var db = CreateContext(new TransactionalDomainEventInterceptor(outbox, publisher));
        var aggregate = new TestAggregate();
        aggregate.AddDomainEvent(new TestDomainEvent(Guid.NewGuid()));
        db.Aggregates.Add(aggregate);

        var act = async () => await db.SaveChangesAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("handler failed");
        // Events were drained before Publish — context must be discarded on failure (scoped UoW).
        aggregate.DomainEvents.Should().BeEmpty();
        db.Aggregates.Local.Should().ContainSingle(); // tracked but never persisted to store
        (await db.Aggregates.CountAsync()).Should().Be(0);
    }
}
