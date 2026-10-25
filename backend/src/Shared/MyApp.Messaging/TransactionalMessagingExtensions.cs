using System.Reflection;
using JasperFx;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MyApp.Messaging.Persistence;
using Shared.Application.Persistence;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.Persistence.Durability;
using Wolverine.RabbitMQ;
using Wolverine.Sqlite;

namespace MyApp.Messaging;

public static class TransactionalMessagingExtensions
{
    public static IHostApplicationBuilder UseTransactionalMessaging(
        this IHostApplicationBuilder builder,
        Action<TransactionalMessagingOptions> configure)
    {
        var options = new TransactionalMessagingOptions();
        configure(options);

        if (options.Stores.Count == 0)
            throw new InvalidOperationException("UseTransactionalMessaging requires at least one AddStore<TDbContext>().");

        if (string.IsNullOrWhiteSpace(options.RabbitMqConnectionString))
            throw new InvalidOperationException("UseTransactionalMessaging requires UseRabbitMq(connectionString).");

        builder.Services.AddScoped<IDbContextCommitFlush, WolverineDbContextCommitFlush>();
        // Generic SavingChanges hook: enroll outbox + dispatch domain events via Mediator
        // (in-process reactions + integration-event publisher handlers live in the modules).
        builder.Services.AddScoped<ISaveChangesInterceptor, TransactionalDomainEventInterceptor>();

        builder.UseWolverine(opts =>
        {
            opts.Durability.Mode = DurabilityMode.Solo;
            opts.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
            opts.Policies.UseDurableLocalQueues();
            opts.UseEntityFrameworkCoreTransactions();

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

            var rabbit = opts.UseRabbitMq(options.RabbitMqConnectionString)
                .AutoProvision();
            if (options.AutoPurgeOnStartup)
                rabbit.AutoPurgeOnStartup();

            foreach (var binding in options.ExchangeBindings)
                rabbit.BindExchange(binding.ExchangeName, ExchangeType.Fanout).ToQueue(binding.QueueName);

            foreach (var pub in options.Publications)
            {
                if (pub.IsExchange)
                {
                    opts.PublishMessage(pub.MessageType)
                        .ToRabbitExchange(pub.Destination)
                        .UseDurableOutbox();
                }
                else
                {
                    opts.PublishMessage(pub.MessageType)
                        .ToRabbitQueue(pub.Destination)
                        .UseDurableOutbox();
                }
            }

            foreach (var queue in options.ListenQueues.Distinct(StringComparer.Ordinal))
            {
                opts.ListenToRabbitQueue(queue)
                    .UseDurableInbox();
            }
        });

        return builder;
    }
}

public sealed class TransactionalMessagingOptions
{
    internal List<MessagingStoreRegistration> Stores { get; } = [];
    internal List<Assembly> HandlerAssemblies { get; } = [];
    internal List<Type> HandlerTypes { get; } = [];
    internal List<MessagePublication> Publications { get; } = [];
    internal List<string> ListenQueues { get; } = [];
    internal List<ExchangeBinding> ExchangeBindings { get; } = [];
    internal string? RabbitMqConnectionString { get; private set; }
    internal bool AutoPurgeOnStartup { get; private set; }

    public TransactionalMessagingOptions UseRabbitMq(string connectionString, bool autoPurgeOnStartup = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        RabbitMqConnectionString = connectionString;
        AutoPurgeOnStartup = autoPurgeOnStartup;
        return this;
    }

    public TransactionalMessagingOptions AddStore<TDbContext>(
        string connectionString,
        MessageStoreRole role = MessageStoreRole.Main)
        where TDbContext : DbContext
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        Stores.Add(new MessagingStoreRegistration(typeof(TDbContext), connectionString, role));
        return this;
    }

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

    public TransactionalMessagingOptions PublishToQueue<TMessage>(string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        Publications.Add(new MessagePublication(typeof(TMessage), queueName, IsExchange: false));
        return this;
    }

    public TransactionalMessagingOptions PublishToExchange<TMessage>(string exchangeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exchangeName);
        Publications.Add(new MessagePublication(typeof(TMessage), exchangeName, IsExchange: true));
        return this;
    }

    public TransactionalMessagingOptions ListenTo(string queueName, string? bindToFanoutExchange = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        ListenQueues.Add(queueName);
        if (!string.IsNullOrWhiteSpace(bindToFanoutExchange))
            ExchangeBindings.Add(new ExchangeBinding(bindToFanoutExchange, queueName));
        return this;
    }

    public static string RequireConnectionString(IConfiguration configuration, string name) =>
        configuration.GetConnectionString(name)
        ?? throw new InvalidOperationException($"Connection string '{name}' is required for messaging.");

    public static string RequireRabbitMq(IConfiguration configuration) =>
        configuration.GetConnectionString("RabbitMq")
        ?? configuration["Messaging:RabbitMq"]
        ?? throw new InvalidOperationException("Connection string 'RabbitMq' (or Messaging:RabbitMq) is required.");
}

internal sealed record MessagingStoreRegistration(
    Type DbContextType,
    string ConnectionString,
    MessageStoreRole Role);

internal sealed record MessagePublication(Type MessageType, string Destination, bool IsExchange);

internal sealed record ExchangeBinding(string ExchangeName, string QueueName);
