using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MyApp.Auth.Jwt;

/// <summary>
/// The authenticated caller as plain values. Build it once in the endpoint with <see cref="From"/>
/// and pass it into commands and queries. Application code never receives a <see cref="ClaimsPrincipal"/>.
/// </summary>
public sealed record Caller(Guid UserId, bool IsAdmin, IReadOnlyCollection<string> Permissions)
{
    public static Caller From(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var userId = Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id)
            ? id
            : Guid.Empty;
        var permissions = principal.FindAll(AuthClaimTypes.Permission)
            .Select(c => c.Value)
            .ToArray();

        return new Caller(userId, principal.IsInRole(Roles.Admin), permissions);
    }

    public bool Has(string permission) =>
        Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
}
