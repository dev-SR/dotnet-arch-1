using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using MyApp.ApiHost;
using MyApp.Auth.Jwt;
using MyApp.Modular;
using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;

public static class ApiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApi(IConfiguration configuration)
        {
            services
                .AddCarterWithAssemblies(ModuleAssemblyCatalog.GetHostAndModuleAssemblies(typeof(AssemblyMarker).Assembly))
                .AddJwtAuthentication(configuration)
                .AddHostAuthorizationFallback()
                .AddApiRateLimiting()
                .AddCorsPolicies(configuration)
                .AddOpenApiDocumentation()
                .AddApiVersioningSupport()
                .AddHealthChecks(configuration)
                .AddSharedExceptionHandling();

            return services;
        }

        private IServiceCollection AddJwtAuthentication(IConfiguration configuration)
        {
            services.AddMyAppAuth(configuration);
            return services;
        }

        private IServiceCollection AddHostAuthorizationFallback()
        {
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });
            return services;
        }

        private IServiceCollection AddCorsPolicies(IConfiguration configuration)
        {
            var allowedOrigins = configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? [];
            services.AddCors(options =>
            {
                options.AddPolicy("Production", policy =>
                    policy.WithOrigins(allowedOrigins)
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials());
                options.AddPolicy("Development", policy =>
                    policy.AllowAnyOrigin()
                        .AllowAnyMethod()
                        .AllowAnyHeader());
            });
            return services;
        }

        private IServiceCollection AddOpenApiDocumentation()
        {
            services.AddOpenApi(options =>
            {
                options.OpenApiVersion = OpenApiSpecVersion.OpenApi3_1;
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Info = new OpenApiInfo
                    {
                        Title = "MyApp API Host",
                        Version = "v1",
                        Description = "Modular monolith composition root — Users, Catalog, Search",
                    };
                    document.AddAuthSecuritySchemes();
                    return Task.CompletedTask;
                });
                options.AddOperationTransformer((operation, context, cancellationToken) =>
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

        private IServiceCollection AddApiVersioningSupport()
        {
            services.AddApiVersioning(options =>
                {
                    options.DefaultApiVersion = new ApiVersion(1, 0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    options.ReportApiVersions = true;
                    options.ApiVersionReader = new UrlSegmentApiVersionReader();
                })
                .AddApiExplorer(options =>
                {
                    options.GroupNameFormat = "'v'VVV";
                    options.SubstituteApiVersionInUrl = true;
                });
            return services;
        }

        private IServiceCollection AddHealthChecks(IConfiguration configuration)
        {
            services.AddHealthChecks();
            return services;
        }
    }
}
