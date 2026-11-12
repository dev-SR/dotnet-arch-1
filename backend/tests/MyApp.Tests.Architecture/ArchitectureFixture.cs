using System.Reflection;
using FluentAssertions;
using Mediator;

namespace MyApp.Tests.Architecture;

/// <summary>
/// Shared assembly sets and helpers for the architecture rules.
/// Assemblies are referenced by marker type, so renaming a project breaks compilation here
/// instead of silently removing it from the rules.
/// </summary>
public static class ArchitectureFixture
{
    public static readonly Assembly[] SharedAssemblies =
    [
        typeof(IMediator).Assembly,                                      // Mediator
        typeof(Shared.Domain.Aggregate).Assembly,                        // Shared.Domain
        typeof(Shared.Common.Errors.ErrorOr<>).Assembly,                 // Shared.Common
        typeof(Shared.Application.Abstractions.Commands.ICommand).Assembly, // Shared.Application
        typeof(MyApp.Modular.IUserContext).Assembly,                     // MyApp.Modular
        typeof(MyApp.Auth.Jwt.Permissions).Assembly,                     // MyApp.Auth.Jwt
        typeof(MyApp.Messaging.IntegrationEvent).Assembly,               // MyApp.Messaging
    ];

    public static readonly Assembly[] ModuleAssemblies =
    [
        typeof(MyApp.Modules.Catalog.CatalogModule).Assembly,
        typeof(MyApp.Modules.Users.UsersModule).Assembly,
        typeof(MyApp.Modules.Search.SearchModule).Assembly,
    ];

    public static readonly Assembly ApiHostAssembly = typeof(MyApp.ApiHost.AssemblyMarker).Assembly;

    /// <summary>Extracted services (not ApiHost). They must not depend on modules or each other.</summary>
    public static readonly IReadOnlyDictionary<string, Assembly> ServiceAssemblies =
        new Dictionary<string, Assembly>
        {
            ["MyApp.OrdersApi"] = typeof(MyApp.OrdersApi.AssemblyMarker).Assembly,
            ["MyApp.InvoicesApi"] = typeof(MyApp.InvoicesApi.AssemblyMarker).Assembly,
            ["MyApp.ShippingApi"] = typeof(MyApp.ShippingApi.AssemblyMarker).Assembly,
        };

    public static IEnumerable<Assembly> AllProductAssemblies =>
        SharedAssemblies
            .Concat(ModuleAssemblies)
            .Concat(ServiceAssemblies.Values)
            .Append(ApiHostAssembly)
            .Distinct();

    public static IEnumerable<Type> ConcreteTypes(IEnumerable<Assembly> assemblies) =>
        assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract && !t.IsCompilerGenerated());

    public static bool IsCompilerGenerated(this Type t) =>
        t.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).Length > 0;

    /// <summary>Type name without the generic arity suffix (<c>Foo`1</c> → <c>Foo</c>).</summary>
    public static string BaseName(this Type t) => t.Name.Split('`')[0];

    public static bool ImplementsGeneric(this Type t, Type genericDefinition) =>
        t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == genericDefinition);

    /// <summary>Repository root (the folder containing MyApp.slnx), found from the test output directory.</summary>
    public static string RepoRoot
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "MyApp.slnx")))
                dir = dir.Parent;
            return dir?.FullName ?? throw new InvalidOperationException("MyApp.slnx not found above the test output.");
        }
    }

    /// <summary>All non-generated .cs files under src/, excluding build output.</summary>
    public static IEnumerable<string> SourceFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>Removes // line comments so rules on code ignore prose.</summary>
    public static string StripLineComments(string source) =>
        string.Join('\n', source.Split('\n').Select(line =>
        {
            var i = line.IndexOf("//", StringComparison.Ordinal);
            return i >= 0 ? line[..i] : line;
        }));
}
