using System.Reflection;
using Microsoft.Extensions.DependencyModel;

namespace MyApp.Modular;

/// <summary>
/// Discovers module assemblies by convention: every runtime library whose name
/// starts with <see cref="ModuleAssemblyPrefix"/> (e.g. <c>MyApp.Modules.Users</c>).
/// Requires a ProjectReference from the host so the library appears in deps.json.
/// </summary>
public static class ModuleAssemblyCatalog
{
    public const string ModuleAssemblyPrefix = "MyApp.Modules.";

    /// <summary>All <c>MyApp.Modules.*</c> assemblies referenced by <paramref name="hostAssembly"/>.</summary>
    public static Assembly[] GetModuleAssemblies(Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(hostAssembly);

        var dependencyContext = DependencyContext.Load(hostAssembly)
            ?? DependencyContext.Default
            ?? throw new InvalidOperationException(
                "DependencyContext is unavailable — cannot discover module assemblies.");

        return dependencyContext.RuntimeLibraries
            .Where(lib => lib.Name.StartsWith(ModuleAssemblyPrefix, StringComparison.Ordinal))
            .SelectMany(lib => lib.GetDefaultAssemblyNames(dependencyContext))
            .Select(Assembly.Load)
            .Distinct()
            .ToArray();
    }

    /// <summary>Host assembly plus every discovered module (Mediator / validators / Carter).</summary>
    public static Assembly[] GetHostAndModuleAssemblies(Assembly hostAssembly)
    {
        ArgumentNullException.ThrowIfNull(hostAssembly);

        return new[] { hostAssembly }
            .Concat(GetModuleAssemblies(hostAssembly))
            .Distinct()
            .ToArray();
    }
}
