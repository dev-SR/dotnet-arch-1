using Carter;
using FluentAssertions;
using Mediator;
using NetArchTest.Rules;

namespace MyApp.Tests.Architecture;

/// <summary>Dependency direction between kit, modules, services, and hosts (ARCHITECTURE.md §3, §12).</summary>
public class LayeringRules
{
    private const string ModulesNs = "MyApp.Modules";
    private const string ApiHostNs = "MyApp.ApiHost";
    private static readonly string[] ServiceNamespaces = ["MyApp.OrdersApi", "MyApp.InvoicesApi", "MyApp.ShippingApi"];

    [Fact]
    public void Shared_kit_does_not_reference_modules_or_hosts_at_assembly_level()
    {
        // Assembly references, not string scans: ModuleAssemblyCatalog holds the "MyApp.Modules." prefix as a constant
        // for discovery, which is not a dependency.
        var forbidden = ArchitectureFixture.ModuleAssemblies.Select(a => a.GetName().Name!)
            .Concat(ArchitectureFixture.ServiceAssemblies.Keys)
            .Append(ArchitectureFixture.ApiHostAssembly.GetName().Name!)
            .ToHashSet(StringComparer.Ordinal);

        var offenders = ArchitectureFixture.SharedAssemblies
            .SelectMany(a => a.GetReferencedAssemblies())
            .Select(n => n.Name!)
            .Where(n => forbidden.Contains(n) || n.StartsWith(ModulesNs + ".", StringComparison.Ordinal))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty("Shared is a kernel; it must not reference modules, services, or hosts");
    }

    [Fact]
    public void Domain_kernel_has_no_web_or_persistence_dependencies()
    {
        // Domain events implement Mediator's INotification by design, so Mediator is allowed here.
        var result = Types.InAssembly(typeof(Shared.Domain.Aggregate).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore", "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Shared.Domain holds aggregates and events; it must stay free of web and EF types. Failing: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("Catalog", "MyApp.Modules.Catalog", "MyApp.Modules.Users")]
    [InlineData("Catalog", "MyApp.Modules.Catalog", "MyApp.Modules.Search")]
    [InlineData("Users", "MyApp.Modules.Users", "MyApp.Modules.Catalog")]
    [InlineData("Users", "MyApp.Modules.Users", "MyApp.Modules.Search")]
    [InlineData("Search", "MyApp.Modules.Search", "MyApp.Modules.Users")]
    [InlineData("Search", "MyApp.Modules.Search", "MyApp.Modules.Catalog")]
    public void Module_does_not_reference_another_module(string name, string moduleNs, string otherNs)
    {
        var assembly = ArchitectureFixture.ModuleAssemblies.Single(a => a.GetName().Name == $"MyApp.Modules.{name}");

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOn(otherNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"{moduleNs} must talk to {otherNs} only through integration events, never direct references.");
    }

    [Fact]
    public void Modules_do_not_depend_on_hosts_or_services()
    {
        var result = Types.InAssemblies(ArchitectureFixture.ModuleAssemblies)
            .ShouldNot()
            .HaveDependencyOnAny([ApiHostNs, .. ServiceNamespaces])
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Modules are libraries composed by ApiHost; they must not depend on a host or an extracted service. Failing: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Theory]
    [InlineData("MyApp.OrdersApi", new[] { "MyApp.InvoicesApi", "MyApp.ShippingApi", "MyApp.ApiHost", "MyApp.Modules" })]
    [InlineData("MyApp.InvoicesApi", new[] { "MyApp.OrdersApi", "MyApp.ShippingApi", "MyApp.ApiHost", "MyApp.Modules" })]
    [InlineData("MyApp.ShippingApi", new[] { "MyApp.OrdersApi", "MyApp.InvoicesApi", "MyApp.ApiHost", "MyApp.Modules" })]
    public void Extracted_service_is_isolated_from_other_services_and_modules(string service, string[] forbidden)
    {
        var assembly = ArchitectureFixture.ServiceAssemblies[service];

        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            $"{service} owns its database and talks to other contexts only through messages. Failing: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }

    [Fact]
    public void Endpoints_do_not_reach_persistence_or_messaging()
    {
        var result = Types.InAssemblies(ArchitectureFixture.AllProductAssemblies)
            .That()
            .ImplementInterface(typeof(ICarterModule))
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Wolverine", "MyApp.Messaging")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Endpoints are transport only. No DbContext, outbox, or broker types. Failing: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Handlers_do_not_reach_outbox_or_http()
    {
        var result = Types.InAssemblies(ArchitectureFixture.AllProductAssemblies)
            .That()
            .ImplementInterface(typeof(IRequestHandler<,>))
            .ShouldNot()
            .HaveDependencyOnAny("Wolverine", "Microsoft.AspNetCore.Http")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Handlers publish through domain events (not the outbox directly) and never touch HttpContext. Failing: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Only_domain_event_bridges_and_consumers_use_the_outbox()
    {
        // Allowed: Publish*IntegrationHandler bridges, consumers, and the messaging/persistence plumbing.
        var result = Types.InAssemblies(ArchitectureFixture.ModuleAssemblies.Concat(ArchitectureFixture.ServiceAssemblies.Values))
            .That()
            .DoNotHaveNameMatching("^Publish")
            .And()
            .DoNotResideInNamespace(".Consumers")
            .ShouldNot()
            .HaveDependencyOn("Wolverine.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "The transactional outbox is used only from integration-event bridges (Publish*IntegrationHandler) and consumers. Failing: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Consumers_do_not_depend_on_http_or_mediator()
    {
        var result = Types.InAssemblies(ArchitectureFixture.ModuleAssemblies.Concat(ArchitectureFixture.ServiceAssemblies.Values))
            .That()
            .ResideInNamespaceContaining(".Consumers.")
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.AspNetCore.Http", "Carter", "Mediator")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Consumers are message-driven, not HTTP or mediator entry points. Failing: {0}",
            string.Join(", ", result.FailingTypeNames ?? []));
    }
}
