namespace MyApp.Infrastructure;

public static class WebApplicationExtensions
{
    /// <summary>Host-level startup hooks (module migrate/seed runs in UseXxxModule).</summary>
    public static WebApplication UseInfrastructure(this WebApplication app) => app;
}
