using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using MyApp.Auth.Jwt;
using MyApp.Modular;
using Shared.Common.Exceptions.Http;

namespace MyApp.OrdersApi.Presentation;

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
                    Title = "Orders API",
                    Version = "v1",
                    Description = "Cart, checkout, and orders. Product catalog is projected from Catalog via RabbitMQ.",
                };
                document.AddAuthSecuritySchemes(
                    "Paste access_token from AuthApi POST /auth/login (via gateway).");
                return Task.CompletedTask;
            });
            options.AddOperationTransformer((operation, context, _) =>
            {
                var allowAnonymous = context.Description.ActionDescriptor.EndpointMetadata
                    .OfType<IAllowAnonymous>().Any();
                if (!allowAnonymous)
                {
                    operation.Security =
                    [
                        new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference("Bearer")] = [],
                        },
                    ];
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }
}
