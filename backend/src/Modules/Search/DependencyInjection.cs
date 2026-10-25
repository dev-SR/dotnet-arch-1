using Elastic.Clients.Elasticsearch;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MyApp.Modules.Search;

public static class DependencyInjection
{
    public static IServiceCollection AddSearchModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var uri = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
        services.AddSingleton(_ => new ElasticsearchClient(new Uri(uri)));
        return services;
    }
}
