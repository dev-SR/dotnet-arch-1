using System.Reflection;
using FluentAssertions;
using Mediator;
using MyApp.Messaging;
using Shared.Domain;

namespace MyApp.Tests.Architecture;

/// <summary>Aggregates, domain events, integration events, and the constants they depend on (ARCHITECTURE.md §8, §11).</summary>
public class DomainAndMessagingRules
{
    private static IEnumerable<Type> AllConcrete => ArchitectureFixture.ConcreteTypes(ArchitectureFixture.AllProductAssemblies);

    [Fact]
    public void Aggregates_have_a_non_public_parameterless_constructor_for_EF()
    {
        var aggregates = AllConcrete.Where(t => t.IsSubclassOf(typeof(Aggregate))).ToList();
        aggregates.Should().NotBeEmpty("the aggregate scan must find the domain aggregates");

        var bad = aggregates
            .Where(t => t.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
                .Any(c => c.GetParameters().Length == 0))
            .Select(t => t.FullName!)
            .ToArray();

        bad.Should().BeEmpty("aggregates expose only factories; EF gets a private/protected constructor");
    }

    [Fact]
    public void Aggregates_expose_no_public_setters()
    {
        // State changes go through factories and domain methods, never through public setters.
        var bad = AllConcrete
            .Where(t => t.IsSubclassOf(typeof(Aggregate)))
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true })
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToArray();

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Domain_events_live_under_an_Events_folder_and_are_named_DomainEvent()
    {
        var events = AllConcrete.Where(t => t.IsSubclassOf(typeof(DomainEvent))).ToList();
        events.Should().NotBeEmpty();

        var misplaced = events
            .Where(t => t.Namespace is null || !t.Namespace.Contains(".Events", StringComparison.Ordinal))
            .Select(t => t.FullName);
        misplaced.Should().BeEmpty("domain events sit in {UseCase}/Events/ next to their bridges");

        events.Where(t => !t.Name.EndsWith("DomainEvent", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Should().BeEmpty();
    }

    [Fact]
    public void Integration_events_are_shared_contracts_named_IntegrationEvent()
    {
        var events = AllConcrete.Where(t => t.IsSubclassOf(typeof(IntegrationEvent))).ToList();
        events.Should().NotBeEmpty();

        events.Where(t => t.Namespace is null || !t.Namespace.StartsWith("MyApp.Messaging", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Should().BeEmpty("integration events are contracts shared by publisher and consumers, so they live in MyApp.Messaging");

        events.Where(t => !t.Name.EndsWith("IntegrationEvent", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Should().BeEmpty();
    }

    [Fact]
    public void Domain_event_notification_handlers_that_publish_integration_events_are_named_Publish()
    {
        var bridges = AllConcrete
            .Where(t => t.GetInterfaces().Any(i => i.IsGenericType
                                                   && i.GetGenericTypeDefinition() == typeof(INotificationHandler<>)
                                                   && i.GetGenericArguments()[0].IsSubclassOf(typeof(DomainEvent))))
            .Where(t => t.GetMethods().Any(m => m.Name == "Handle" && m.ReturnType == typeof(ValueTask)
                                                && m.GetParameters().Length == 2))
            .ToList();

        // Every domain-event handler that calls the outbox must be a Publish*IntegrationHandler.
        var outboxUsers = bridges.Where(t => t.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Any(p => p.ParameterType.Name == "IDbContextOutbox"))
            .ToList();

        outboxUsers.Should().NotBeEmpty("the bridge scan must find the integration publishers");
        outboxUsers.Where(t => !t.Name.StartsWith("Publish", StringComparison.Ordinal) || !t.Name.EndsWith("IntegrationHandler", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Should().BeEmpty("domain→integration bridges are Publish*IntegrationHandler");
    }

    /// <summary>The kit defines one set of permission, role, and claim constants (MyApp.Auth.Jwt).</summary>
    [Fact]
    public void Permission_role_and_claim_constants_are_defined_once()
    {
        var names = new HashSet<string>(StringComparer.Ordinal) { "Permissions", "Roles", "AuthClaims", "AuthClaimTypes" };
        var found = ArchitectureFixture.AllProductAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && t.IsSealed && t.IsAbstract && names.Contains(t.Name) && t.DeclaringType is null)
            .Select(t => t.FullName!)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        found.Should().BeEquivalentTo(new[]
        {
            "MyApp.Auth.Jwt.AuthClaimTypes",
            "MyApp.Auth.Jwt.Permissions",
            "MyApp.Auth.Jwt.Roles",
        });
    }
}
