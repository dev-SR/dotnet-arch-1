using Asp.Versioning;
using Microsoft.OpenApi;
using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;
//The API layer registers HTTP-specific concerns: authentication, authorization, CORS, Swagger, and middleware.

// src/MyApp.Api/DependencyInjection.cs
public static class ApiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApi(IConfiguration configuration)
        {
            services
                .AddCarter()
                .AddAuthenticationAndAuthorization(configuration)
                .AddCorsPolicies(configuration)
                .AddOpenApiDocumentation()
                .AddApiVersioningSupport()
                .AddHealthChecks(configuration)
                .AddSharedExceptionHandling();

            return services;
        }

        private IServiceCollection AddAuthenticationAndAuthorization(IConfiguration configuration)
        {

            return services;
        }

        private IServiceCollection AddCorsPolicies(IConfiguration configuration)
        {
            var allowedOrigins = configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>() ?? Array.Empty<string>();
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
                options.OpenApiVersion = Microsoft.OpenApi.OpenApiSpecVersion.OpenApi3_1;
                // Add document transformer for info
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Info = new OpenApiInfo
                    {
                        Title = "MyApp API",
                        Version = "v1",
                        Description = "Production API for MyApp",
                        Contact = new OpenApiContact
                        {
                            Name = "API Support",
                            Email = "api@myapp.com"
                        }
                    };
                    return Task.CompletedTask;
                });
                // Add security scheme
                options.AddDocumentTransformer((document, context, cancellationToken) =>
                {
                    document.Components ??= new OpenApiComponents();
                    document.Components.SecuritySchemes?.Add("Bearer", new OpenApiSecurityScheme
                    {
                        Type = SecuritySchemeType.Http,
                        Scheme = "Bearer",
                        BearerFormat = "JWT",
                        Description = "Enter your JWT token"
                    });
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
            // .AddDbContextCheck<AppDbContext>()
            // .AddRedis("Redis", tags: new[] { "cache" })
            // .AddUrlGroup(
            //     new Uri(configuration["ExternalApis:PaymentGateway:HealthUrl"]!),
            //     name: "payment-gateway",
            //     tags: new[] { "external" });
            return services;
        }
    }
}
