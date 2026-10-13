using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Errors;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth;

/// <summary>
/// AuthApi-only JWT events (ban / token_version). Crypto validation comes from
/// <c>AddMyAppAuth</c> in MyApp.Auth.Jwt.
/// </summary>
public sealed class ConfigureJwtBearer : IConfigureNamedOptions<JwtBearerOptions>
{
    public void Configure(string? name, JwtBearerOptions options)
    {
        if (name != JwtBearerDefaults.AuthenticationScheme) return;
        Configure(options);
    }

    public void Configure(JwtBearerOptions options)
    {
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
        var sub = context.Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub)
                  ?? context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (sub is null || !Guid.TryParse(sub, out var userId))
        {
            context.Fail("Missing sub");
            return;
        }

        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var user = await db.Users
            .Apply(new UserByIdSpecification(userId))
            .Select(u => new { u.IsBanned, u.PermissionVersion })
            .SingleOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user is null || user.IsBanned)
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.ACCOUNT_DISABLED", "Account is locked or disabled.");
            context.Fail("banned");
            return;
        }

        var version = context.Principal!.FindFirstValue(AuthClaims.TokenVersion);
        if (version is null || !int.TryParse(version, out var tv) || tv != user.PermissionVersion)
        {
            context.HttpContext.Items[JwtBearerProblem.ChallengeErrorItemKey] =
                Error.Unauthorized("AUTH.TOKEN_REVOKED", "Token has been revoked.");
            context.Fail("token_version");
        }
    }

    private static Error MapChallengeError(JwtBearerChallengeContext context) =>
        context.AuthenticateFailure is not null
            ? Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid or expired access token.")
            : Error.Unauthorized("AUTH.UNAUTHENTICATED", "Authentication is required.");
}
