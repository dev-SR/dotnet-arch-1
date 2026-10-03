using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using MyApp.Config;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Errors;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth;

public sealed class ConfigureJwtBearer(IOptions<JwtOptions> jwt) : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
        var o = jwt.Value;
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret));

        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = o.Issuer,
            ValidateAudience = true,
            ValidAudience = o.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.Sub,
            RoleClaimType = AuthClaims.Role,
        };

        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = OnTokenValidatedAsync,
            OnAuthenticationFailed = _ => Task.CompletedTask,
            OnChallenge = OnChallengeAsync,
            OnForbidden = OnForbiddenAsync,
        };
    }

    private static async Task OnChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        if (context.HttpContext.Response.HasStarted) return;

        var error = context.HttpContext.Items.TryGetValue(JwtBearerProblem.ChallengeErrorItemKey, out var boxed)
                    && boxed is Error stored
            ? stored
            : MapChallengeError(context);
        await JwtBearerProblem.WriteAsync(context.HttpContext, error);
    }

    private static async Task OnForbiddenAsync(ForbiddenContext context)
    {
        if (context.HttpContext.Response.HasStarted) return;
        await JwtBearerProblem.WriteAsync(
            context.HttpContext,
            Error.Forbidden("AUTH.FORBIDDEN", "You are not allowed to perform this action."));
    }

    private static async Task OnTokenValidatedAsync(TokenValidatedContext context)
    {
        // sub ~ userId
        var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.UNAUTHORIZED", "Authentication is required.");
            context.Fail("Missing subject.");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .Apply(new UserByIdSpecification(userId))
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user is null || user.IsBanned)
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.ACCOUNT_DISABLED", "Account is locked or disabled.");
            context.Fail("User banned or missing.");
            return;
        }

        var version = context.Principal?.FindFirst(AuthClaims.TokenVersion)?.Value;
        if (version is null || !int.TryParse(version, out var tv) || tv != user.PermissionVersion)
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.UNAUTHORIZED", "Authentication is required.");
            context.Fail("Token version mismatch.");
        }
    }

    private static Error MapChallengeError(JwtBearerChallengeContext context)
    {
        var ex = context.AuthenticateFailure
                 ?? context.HttpContext.Features.Get<IAuthenticateResultFeature>()?.AuthenticateResult?.Failure;

        if (ex is SecurityTokenExpiredException)
            return Error.Unauthorized("AUTH.TOKEN_EXPIRED", "The access token has expired.");

        if (ex is SecurityTokenException)
            return Error.Unauthorized("AUTH.TOKEN_INVALID", "The access token is invalid.");

        return Error.Unauthorized("AUTH.UNAUTHORIZED", "Authentication is required.");
    }
}
