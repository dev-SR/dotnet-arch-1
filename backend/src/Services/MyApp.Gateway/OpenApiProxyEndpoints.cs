using System.Text.Json;

namespace MyApp.Gateway;

/// <summary>Proxies upstream OpenAPI docs so Scalar can federate them on the gateway port.</summary>
public static class OpenApiProxyEndpoints
{
    public const string AuthClient = "auth-openapi";
    public const string InvoicesClient = "invoices-openapi";

    public static IServiceCollection AddOpenApiProxyClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var authBase = configuration["OpenApi:AuthBaseUrl"] ?? "http://localhost:5101";
        var invoicesBase = configuration["OpenApi:InvoicesBaseUrl"] ?? "http://localhost:5102";

        services.AddHttpClient(AuthClient, c => c.BaseAddress = new Uri(authBase));
        services.AddHttpClient(InvoicesClient, c => c.BaseAddress = new Uri(invoicesBase));
        return services;
    }

    public static IEndpointRouteBuilder MapOpenApiProxyEndpoints(this IEndpointRouteBuilder app)
    {
        // Root paths (preferred) + /scalar/... aliases for relative URL resolution from the UI.
        MapProxy(app, "/openapi/auth/v1.json", AuthClient);
        MapProxy(app, "/scalar/openapi/auth/v1.json", AuthClient);
        MapProxy(app, "/openapi/invoices/v1.json", InvoicesClient);
        MapProxy(app, "/scalar/openapi/invoices/v1.json", InvoicesClient);
        return app;
    }

    private static void MapProxy(IEndpointRouteBuilder app, string route, string clientName)
    {
        app.MapGet(route, (IHttpClientFactory http, IConfiguration config, CancellationToken ct) =>
            ProxyAsync(http, clientName, "/openapi/v1.json", config, ct));
    }

    private static async Task<IResult> ProxyAsync(
        IHttpClientFactory httpClientFactory,
        string clientName,
        string path,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(clientName);
        try
        {
            using var response = await client.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return Results.Problem(
                    title: "Upstream OpenAPI unavailable",
                    detail: $"{clientName} returned {(int)response.StatusCode} for {path}.",
                    statusCode: StatusCodes.Status502BadGateway);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var gatewayBase = configuration["OpenApi:GatewayPublicUrl"] ?? "http://localhost:5100";
            json = RewriteServers(json, gatewayBase);
            return Results.Content(json, "application/json");
        }
        catch (HttpRequestException ex)
        {
            return Results.Problem(
                title: "Upstream OpenAPI unavailable",
                detail: $"{clientName} unreachable: {ex.Message}",
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static string RewriteServers(string openApiJson, string gatewayBase)
    {
        using var doc = JsonDocument.Parse(openApiJson);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            var wroteServers = false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("servers"))
                {
                    WriteGatewayServers(writer, gatewayBase);
                    wroteServers = true;
                }
                else
                {
                    prop.WriteTo(writer);
                }
            }

            if (!wroteServers)
                WriteGatewayServers(writer, gatewayBase);

            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteGatewayServers(Utf8JsonWriter writer, string gatewayBase)
    {
        writer.WritePropertyName("servers");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("url", gatewayBase.TrimEnd('/'));
        writer.WriteEndObject();
        writer.WriteEndArray();
    }
}
