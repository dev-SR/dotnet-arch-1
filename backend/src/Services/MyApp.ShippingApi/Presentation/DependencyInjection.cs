using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using MyApp.Auth.Jwt;
using MyApp.Modular;
using Shared.Common.Exceptions.Http;

namespace MyApp.ShippingApi.Presentation;

public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var assembly = typeof(AssemblyMarker).Assembly;

        services.AddSharedExceptionHandling();
        services.AddMyAppAuth(configuration);
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        services.AddCarterWithAssemblies(assembly);
        services.AddOpenApiDocumentation();

        return services;
    }

    private static IServiceCollection AddOpenApiDocumentation(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Shipping API",
                    Version = "v1",
                    Description = "Shipments created from OrderPlaced integration events.",
                };
                document.AddAuthSecuritySchemes(
                    "Paste access_token from AuthApi POST /auth/login (via gateway).");
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
