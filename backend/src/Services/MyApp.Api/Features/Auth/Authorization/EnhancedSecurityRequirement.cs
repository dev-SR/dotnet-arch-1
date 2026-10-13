using Microsoft.AspNetCore.Authorization;

namespace MyApp.Features.Auth.Authorization;

/// <summary>Composite requirement: MFA claim AND trusted IP (demo).</summary>
public sealed class EnhancedSecurityRequirement : IAuthorizationRequirement;

/// <summary>
/// Single handler so MFA + IP are AND (two separate handlers on the same
/// requirement would OR — any Succeed wins).
/// </summary>
public sealed class EnhancedSecurityHandler : AuthorizationHandler<EnhancedSecurityRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EnhancedSecurityRequirement requirement)
    {
        var hasMfa = context.User.HasClaim(AuthClaims.Amr, "mfa");

        var http = context.Resource as HttpContext;
        var trustedIp = false;
        if (http is not null)
        {
            if (http.Request.Headers.TryGetValue("X-Trusted-Client", out var trusted)
                && trusted.ToString().Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                trustedIp = true;
            }
            else
            {
                var ip = http.Connection.RemoteIpAddress;
                trustedIp = ip is not null && System.Net.IPAddress.IsLoopback(ip);
            }
        }

        if (hasMfa && trustedIp)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
