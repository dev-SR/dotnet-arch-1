
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MyApp.Features.Auth;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
}

public static class AuthClaims
{
    public const string Role = "role";
    public const string TokenVersion = "token_version";

    public static Guid GetUserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
