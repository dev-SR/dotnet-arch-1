using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi;
using MyApp.Auth.Jwt;
using MyApp.Features.Auth;
using MyApp.Features.Auth.Authorization;
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
                .AddJwtAuthentication(configuration)
                .AddAuthorizationPolicies()
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
            // Shared HS256 + permission policies (Hospital-style AddMyAppAuth).
            services.AddMyAppAuth(configuration);
            // AuthApi-only: ban / token_version checks after crypto validation.
            services.ConfigureOptions<ConfigureJwtBearer>();
            return services;
        }

        private IServiceCollection AddAuthorizationPolicies()
        {
            // AddMyAppAuth already called AddAuthorization + PermissionPolicyProvider.
            services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();

                options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));

                options.AddPolicy(Policies.WorkingHours, p =>
                    p.Requirements.Add(new WorkingHoursRequirement(9, 18)));

                options.AddPolicy(Policies.EnhancedSecurity, p =>
                    p.Requirements.Add(new EnhancedSecurityRequirement()));
            });

            services.AddSingleton<IAuthorizationHandler, WorkingHoursHandler>();
            services.AddSingleton<IAuthorizationHandler, EnhancedSecurityHandler>();
            services.AddSingleton<IAuthorizationHandler, ResourcePermissionHandler>();

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
