using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MyApp.Auth.Jwt;

public static class AuthServiceCollectionExtensions
{
    /// <summary>
    /// Hospital-style one-liner: HS256 JWT validation + claim-only permission policies.
    /// Does <b>not</b> register DB permission / token-version checkers — every host validates
    /// the shared secret and trusts embedded <c>permission</c> claims.
    /// </summary>
    public static IServiceCollection AddMyAppAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                var jwt = configuration.GetSection(JwtOptions.Section).Get<JwtOptions>()
                          ?? throw new InvalidOperationException("Jwt config section missing.");

                if (string.IsNullOrWhiteSpace(jwt.Secret) || Encoding.UTF8.GetByteCount(jwt.Secret) < 32)
                {
                    throw new InvalidOperationException(
                        "Jwt:Secret must be ≥ 256 bits (32 bytes) of UTF-8 entropy.");
                }

                opts.MapInboundClaims = false;
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = JwtRegisteredClaimNames.Sub,
                    RoleClaimType = AuthClaimTypes.Role,
                };
            });

        services.AddMyAppAuthorization();
        return services;
    }

    public static IServiceCollection AddMyAppAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
