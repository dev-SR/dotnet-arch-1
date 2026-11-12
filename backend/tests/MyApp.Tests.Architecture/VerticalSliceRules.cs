using System.Reflection;
using System.Security.Claims;
using Carter;
using FluentAssertions;
using FluentValidation;
using Mediator;
using Shared.Application.Abstractions.Commands;
using Shared.Application.Abstractions.Queries;
using Shared.Common.Errors;
using Shared.Common.Specifications;

namespace MyApp.Tests.Architecture;

/// <summary>Vertical-slice shape: requests, handlers, endpoints, validators (ARCHITECTURE.md §6).</summary>
public class VerticalSliceRules
{
    private static readonly Type RequestHandlerOpen = typeof(IRequestHandler<,>);
    private static readonly Type RequestOpen = typeof(IRequest<>);
    private static readonly Type ErrorOrOpen = typeof(ErrorOr<>);

    private static IEnumerable<Type> AllConcrete => ArchitectureFixture.ConcreteTypes(ArchitectureFixture.AllProductAssemblies);

    private static IEnumerable<Type> Handlers => AllConcrete.Where(t => t.ImplementsGeneric(RequestHandlerOpen));

    private static IEnumerable<Type> Requests => AllConcrete.Where(t =>
        !t.ImplementsGeneric(RequestHandlerOpen)
        && t.ImplementsGeneric(RequestOpen)
        && (typeof(ICommandMarker).IsAssignableFrom(t) || t.ImplementsGeneric(typeof(IQuery<>))));

    [Fact]
    public void Every_request_handler_class_is_named_Handler()
    {
        var bad = Handlers.Where(t => !t.Name.EndsWith("Handler", StringComparison.Ordinal)).Select(t => t.FullName).ToList();
        bad.Should().BeEmpty("handler classes must end with 'Handler' so they are easy to find");
    }

    [Fact]
    public void Every_request_handler_returns_ErrorOr()
    {
        var bad = Handlers
            .Where(h => h.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == RequestHandlerOpen)
                .Any(i => !(i.GetGenericArguments()[1].IsGenericType
                            && i.GetGenericArguments()[1].GetGenericTypeDefinition() == ErrorOrOpen)))
            .Select(h => h.FullName)
            .ToList();

        bad.Should().BeEmpty("expected failures are results; handlers return ErrorOr<T> or ErrorOr<Success>");
    }

    [Fact]
    public void Every_command_and_query_has_exactly_one_handler()
    {
        var handlerCounts = Handlers
            .SelectMany(h => h.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == RequestHandlerOpen)
                .Select(i => i.GetGenericArguments()[0]))
            .GroupBy(r => r)
            .ToDictionary(g => g.Key, g => g.Count());

        var problems = Requests
            .Select(r => (Request: r, Count: handlerCounts.GetValueOrDefault(r)))
            .Where(x => x.Count != 1)
            .Select(x => $"{x.Request.FullName} has {x.Count} handler(s)")
            .ToList();

        problems.Should().BeEmpty("each command or query must have exactly one handler");
    }

    [Fact]
    public void Commands_and_queries_are_named_by_intent()
    {
        var bad = AllConcrete
            .Where(t => typeof(ICommandMarker).IsAssignableFrom(t) && !t.Name.EndsWith("Command", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Concat(AllConcrete
                .Where(t => t.ImplementsGeneric(typeof(IQuery<>)) && !t.Name.EndsWith("Query", StringComparison.Ordinal))
                .Select(t => t.FullName))
            .ToList();

        bad.Should().BeEmpty("commands end with 'Command' and queries end with 'Query'");
    }

    [Fact]
    public void Commands_and_queries_do_not_carry_ClaimsPrincipal_or_HttpContext()
    {
        var offenders = Requests
            .Where(CarriesHttpIdentity)
            .Select(r => r.FullName!)
            .ToArray();

        offenders.Should().BeEmpty("application requests take plain values; endpoints build a Caller from the principal");
    }

    private static bool CarriesHttpIdentity(Type request)
    {
        var parameterTypes = request.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType);
        var propertyTypes = request.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.PropertyType);

        return parameterTypes.Concat(propertyTypes)
            .Any(t => t == typeof(ClaimsPrincipal) || t.Namespace == "Microsoft.AspNetCore.Http");
    }

    [Fact]
    public void Every_carter_endpoint_is_named_Endpoint()
    {
        var bad = AllConcrete
            .Where(t => typeof(ICarterModule).IsAssignableFrom(t) && !t.Name.EndsWith("Endpoint", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        bad.Should().BeEmpty("HTTP surface classes are named <UseCase>Endpoint");
    }

    [Fact]
    public void Every_validator_is_named_Validator()
    {
        var bad = AllConcrete
            .Where(t => t.BaseType is { IsGenericType: true } b
                        && b.GetGenericTypeDefinition() == typeof(AbstractValidator<>)
                        && !t.Name.EndsWith("Validator", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .ToList();

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Every_specification_is_named_Spec_or_Specification()
    {
        var bad = AllConcrete
            .Where(t => t.ImplementsGeneric(typeof(ISpecification<>)))
            .Where(t => !(t.BaseName().EndsWith("Spec", StringComparison.Ordinal)
                          || t.BaseName().EndsWith("Specification", StringComparison.Ordinal)))
            .Select(t => t.FullName)
            .ToList();

        bad.Should().BeEmpty();
    }

    [Fact]
    public void Every_consumer_handler_is_named_Handler_and_lives_under_Consumers()
    {
        var consumers = ArchitectureFixture.ConcreteTypes(ArchitectureFixture.ModuleAssemblies.Concat(ArchitectureFixture.ServiceAssemblies.Values))
            .Where(t => t.Namespace is not null && t.Namespace.Contains(".Consumers.", StringComparison.Ordinal))
            .Where(t => t.GetMethods().Any(m => m.Name == "Handle"))
            .ToList();

        consumers.Should().NotBeEmpty("the consumer scan must find the inbound handlers; if this fails the naming scan is broken");
        consumers.Where(t => !t.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(t => t.FullName)
            .Should().BeEmpty("consumers are named <Event>Handler");
    }
}
