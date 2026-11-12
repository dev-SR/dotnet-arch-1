using System.Text.RegularExpressions;
using FluentAssertions;

namespace MyApp.Tests.Architecture;

/// <summary>
/// Source and layout conventions that reflection cannot see: folder shape, Program.cs contents,
/// and forbidden calls. Comments are stripped before matching, so prose never trips a rule.
/// </summary>
public class SourceConventionRules
{
    private static readonly string[] LayeredHosts = ["MyApp.ApiHost", "MyApp.OrdersApi", "MyApp.InvoicesApi", "MyApp.ShippingApi"];

    // Gateway is a YARP/Scalar host, not a layered application host, so it is excluded from the layout rules.

    private static string Src(string relative) =>
        Path.Combine(ArchitectureFixture.RepoRoot, "src", relative);

    private static IEnumerable<(string Path, string Code)> CodeFiles(IEnumerable<string> files) =>
        files.Select(p => (p, ArchitectureFixture.StripLineComments(File.ReadAllText(p))));

    [Fact]
    public void Layered_hosts_have_Program_and_the_three_layer_folders()
    {
        var missing = new List<string>();
        foreach (var host in LayeredHosts)
        {
            var root = Src($"Services/{host}");
            foreach (var required in new[]
                     {
                         "Program.cs",
                         "Application/DependencyInjection.cs",
                         "Infrastructure/DependencyInjection.cs",
                         "Presentation/DependencyInjection.cs",
                     })
            {
                if (!File.Exists(Path.Combine(root, required)))
                    missing.Add($"{host}/{required}");
            }
        }

        missing.Should().BeEmpty("every layered host has Program.cs and Application/Infrastructure/Presentation");
    }

    [Fact]
    public void Layered_host_Program_only_composes_and_maps_no_routes()
    {
        var forbidden = new[] { "MapGet(", "MapPost(", "MapPut(", "MapDelete(", "AddDbContext", "UseSqlite(", "AddAuthentication(", "AddAuthorization(", "AddOpenApi(" };

        var offenders = new List<string>();
        foreach (var host in LayeredHosts)
        {
            var (path, code) = CodeFiles([Src($"Services/{host}/Program.cs")]).Single();
            foreach (var token in forbidden.Where(code.Contains))
                offenders.Add($"{host}: {token}");
        }

        offenders.Should().BeEmpty("Program.cs chains Add*/Use* and MapCarter() only");
    }

    [Fact]
    public void Layered_hosts_do_not_map_health_inline()
    {
        var offenders = LayeredHosts
            .SelectMany(h => Directory.EnumerateFiles(Src($"Services/{h}"), "*.cs", SearchOption.AllDirectories))
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(p => (p, ArchitectureFixture.StripLineComments(File.ReadAllText(p))))
            .Where(x => x.Item2.Contains("MapGet(\"/health\""))
            .Select(x => x.p)
            .ToList();

        offenders.Should().BeEmpty("health is MapHealthChecks(\"/health\") from Presentation, registered with AddHealthChecks()");
    }

    [Fact]
    public void No_root_level_messaging_extensions_in_hosts()
    {
        var offenders = LayeredHosts
            .SelectMany(h => Directory.EnumerateFiles(Src($"Services/{h}"), "*Messaging*.cs", SearchOption.TopDirectoryOnly))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty("messaging wiring lives under Infrastructure/");
    }

    [Fact]
    public void Schema_is_bootstrapped_only_through_DbContextSchema()
    {
        var offenders = CodeFiles(ArchitectureFixture.SourceFiles())
            .Where(x => x.Code.Contains("EnsureCreated(") || x.Code.Contains("Database.Migrate("))
            .Where(x => !x.Path.EndsWith($"{Path.DirectorySeparatorChar}DbContextSchema.cs", StringComparison.Ordinal))
            .Select(x => x.Path)
            .ToList();

        offenders.Should().BeEmpty("DbContextSchema.Apply is the single place that creates or migrates a database");
    }

    [Fact]
    public void Request_handlers_outside_consumers_never_call_SaveChanges()
    {
        var offenders = CodeFiles(ArchitectureFixture.SourceFiles())
            .Where(x => x.Code.Contains("IRequestHandler<")
                        && !x.Path.Contains($"{Path.DirectorySeparatorChar}Consumers{Path.DirectorySeparatorChar}"))
            .Where(x => x.Code.Contains("SaveChanges"))
            .Select(x => x.Path)
            .ToList();

        offenders.Should().BeEmpty("TransactionBehavior saves and commits command handlers' changes");
    }

    [Fact]
    public void Endpoints_do_not_use_Results_for_domain_outcomes()
    {
        // Diagnostics and the policy demo return Results.* directly by design; they are not domain endpoints.
        var allowed = new[] { "AuthDemoEndpoint.cs", "ErrorDiagnosticsEndpoint.cs" };
        var untypedResults = new Regex(@"(?<![A-Za-z])Results\.", RegexOptions.Compiled);

        var offenders = CodeFiles(ArchitectureFixture.SourceFiles())
            .Where(x => Path.GetFileName(x.Path).EndsWith("Endpoint.cs", StringComparison.Ordinal))
            .Where(x => !allowed.Contains(Path.GetFileName(x.Path)))
            .Where(x => untypedResults.IsMatch(x.Code))
            .Select(x => Path.GetFileName(x.Path))
            .ToList();

        offenders.Should().BeEmpty("endpoints map handler results with Match* / ToProblem, not Results.*");
    }

    [Fact]
    public void Blocking_on_async_only_in_known_startup_exceptions()
    {
        // Development seeding and the Search index bootstrap block once at startup, never on a request.
        var allowed = new[] { "DevelopmentSeederExtensions.cs", "SearchModule.cs" };

        var offenders = CodeFiles(ArchitectureFixture.SourceFiles())
            .Where(x => x.Code.Contains(".GetAwaiter().GetResult()") || x.Code.Contains(".Result;"))
            .Where(x => !allowed.Contains(Path.GetFileName(x.Path)))
            .Select(x => x.Path)
            .ToList();

        offenders.Should().BeEmpty("async code is awaited, not blocked on");
    }

    [Fact]
    public void Test_projects_do_not_contain_production_references_to_each_other_in_src()
    {
        // Guardrail: src/ must never reference tests/.
        var testReference = new Regex(@"Include=""[^""]*tests[\\/]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        var offenders = Directory.EnumerateFiles(Src(""), "*.csproj", SearchOption.AllDirectories)
            .Where(p => testReference.IsMatch(File.ReadAllText(p)))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty("production projects never reference test projects");
    }
}
