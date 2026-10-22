using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApp.Auth.Jwt;

namespace MyApp.Modules.Users.Auth.AuthDemo;

/// <summary>Demo endpoints for WorkingHours / EnhancedSecurity policies (Learn 02).</summary>
public sealed class AuthDemoEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/demo")
            .WithTags("AuthDemo")
            .RequireAuthorization();

        group.MapGet("/working-hours", () => Results.Ok(new { ok = true, policy = Policies.WorkingHours }))
            .RequireAuthorization(Policies.WorkingHours)
            .WithName("DemoWorkingHours");

        group.MapGet("/enhanced-security", () => Results.Ok(new { ok = true, policy = Policies.EnhancedSecurity }))
            .RequireAuthorization(Policies.EnhancedSecurity)
            .WithName("DemoEnhancedSecurity");

        // Re-issue the caller's access token with amr=mfa for EnhancedSecurity demos.
        group.MapPost("/mfa-token", IssueMfaTokenAsync)
            .WithName("DemoMfaToken");
    }

    private static Task<IResult> IssueMfaTokenAsync(
        ClaimsPrincipal principal,
        IOptions<JwtOptions> jwtOptions,
        TimeProvider clock)
    {
        var jwt = jwtOptions.Value;
        var now = clock.GetUtcNow();
        var claims = principal.Claims
            .Where(c => c.Type is not (JwtRegisteredClaimNames.Iat or JwtRegisteredClaimNames.Nbf
                or JwtRegisteredClaimNames.Exp or JwtRegisteredClaimNames.Jti))
            .Select(c => new Claim(c.Type, c.Value))
            .ToList();

        claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")));
        claims.Add(new Claim(JwtRegisteredClaimNames.Iat,
            EpochTime.GetIntDate(now.UtcDateTime).ToString(), ClaimValueTypes.Integer64));
        if (!claims.Any(c => c.Type == AuthClaims.Amr && c.Value == "mfa"))
            claims.Add(new Claim(AuthClaims.Amr, "mfa"));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            NotBefore = now.UtcDateTime,
            Expires = now.UtcDateTime.AddMinutes(jwt.AccessTokenMinutes),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return Task.FromResult(Results.Ok(new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = jwt.AccessTokenMinutes * 60,
        }));
    }
}
