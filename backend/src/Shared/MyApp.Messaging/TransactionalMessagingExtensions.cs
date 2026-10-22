using System.Reflection;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Application.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.Sqlite;

namespace MyApp.Messaging;

/// <summary>
/// Host-neutral Wolverine EF + SQLite outbox/inbox wiring for modular hosts and microservices.
/// </summary>
public static class TransactionalMessagingExtensions
{
    /// <summary>
    /// Call once on the host. Registers Wolverine with durable local queues, EF transactions,
    /// and message stores enrolled per module DbContext (ancillary stores for modular monoliths).
    /// </summary>
    public static IHostApplicationBuilder UseTransactionalMessaging(
        this IHostApplicationBuilder builder,
        Action<TransactionalMessagingOptions> configure)
    {
        var options = new TransactionalMessagingOptions();
        configure(options);

        if (options.Stores.Count == 0)
            throw new InvalidOperationException("UseTransactionalMessaging requires at least one AddStore<TDbContext>().");

        builder.Services.AddScoped<IDbContextCommitFlush, WolverineDbContextCommitFlush>();

        builder.UseWolverine(opts =>
        {
            // Single-node: own the durable local inbox immediately (dev + WebApplicationFactory).
            opts.Durability.Mode = DurabilityMode.Solo;
            opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            opts.Policies.UseDurableLocalQueues();
            opts.UseEntityFrameworkCoreTransactions();

            // Avoid picking up Mediator IRequestHandler.Handle methods as Wolverine handlers.
            opts.Discovery.DisableConventionalDiscovery();
            foreach (var assembly in options.HandlerAssemblies)
                opts.Discovery.IncludeAssembly(assembly);
            foreach (var type in options.HandlerTypes)
                opts.Discovery.IncludeType(type);

            var main = options.Stores.FirstOrDefault(s => s.Role == MessageStoreRole.Main)
                       ?? options.Stores[0];

            foreach (var store in options.Stores)
            {
                var role = ReferenceEquals(store, main) ? MessageStoreRole.Main : MessageStoreRole.Ancillary;
                opts.PersistMessagesWithSqlite(store.ConnectionString, role: role)
                    .Enroll(store.DbContextType);
            }

            // Local in-process delivery for integration events (broker comes later).
            opts.PublishAllMessages().Locally();
        });

        return builder;
    }
}

public sealed class TransactionalMessagingOptions
{
    internal List<MessagingStoreRegistration> Stores { get; } = [];
    internal List<Assembly> HandlerAssemblies { get; } = [];
    internal List<Type> HandlerTypes { get; } = [];

    /// <summary>Enroll a module/service DbContext with its SQLite connection for outbox/inbox.</summary>
    public TransactionalMessagingOptions AddStore<TDbContext>(
        string connectionString,
        MessageStoreRole role = MessageStoreRole.Main)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Stores.Add(new MessagingStoreRegistration(typeof(TDbContext), connectionString, role));
        return this;
    }

    /// <summary>Only scan these assemblies for Wolverine handlers (not Mediator handlers).</summary>
    public TransactionalMessagingOptions IncludeHandlersFrom(Assembly assembly)
    {
        HandlerAssemblies.Add(assembly);
        return this;
    }

    public TransactionalMessagingOptions IncludeHandler<THandler>()
        where THandler : class
    {
        HandlerTypes.Add(typeof(THandler));
        return this;
    }

    /// <summary>Resolve connection string from configuration (throws if missing).</summary>
    public static string RequireConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is required for messaging.");
}

internal sealed record MessagingStoreRegistration(
    Type DbContextType,
    string ConnectionString,
    MessageStoreRole Role);
