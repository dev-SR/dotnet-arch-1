using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using MyApp.Features.Auth;
using Shared.Common.Exceptions.Http;

namespace MyApp.Presentation;

public static class ApiServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddApi(IConfiguration configuration)
        {
            services
                .AddCarter()
                .AddJwtAuthentication()
                .AddAuthorizationPolicies()
                .AddApiRateLimiting()
                .AddCorsPolicies(configuration)
                .AddOpenApiDocumentation()
                .AddApiVersioningSupport()
                .AddHealthChecks(configuration)
                .AddSharedExceptionHandling();

            return services;
        }


        private IServiceCollection AddJwtAuthentication()
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(); // options come from ConfigureJwtBearer

            services.ConfigureOptions<ConfigureJwtBearer>();
            return services;
        }

        private IServiceCollection AddAuthorizationPolicies()
        {
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));
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
                        Title = "MyApp API",
                        Version = "v1",
                        Description = "Production API for MyApp",
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
