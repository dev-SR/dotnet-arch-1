using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using MyApp.Auth.Jwt;

namespace MyApp.Features.Auth;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string WorkingHours = "WorkingHours";
    public const string EnhancedSecurity = "EnhancedSecurity";
}

/// <summary>Forwards to shared <see cref="MyApp.Auth.Jwt.Permissions"/> (gateway + ProductsApi).</summary>
public static class Permissions
{
    public const string Prefix = MyApp.Auth.Jwt.Permissions.Prefix;

    public static class Products
    {
        public const string View = MyApp.Auth.Jwt.Permissions.Products.View;
        public const string Create = MyApp.Auth.Jwt.Permissions.Products.Create;
        public const string Edit = MyApp.Auth.Jwt.Permissions.Products.Edit;
        public const string Delete = MyApp.Auth.Jwt.Permissions.Products.Delete;
    }

    public static class Users
    {
        public const string View = MyApp.Auth.Jwt.Permissions.Users.View;
        public const string Ban = MyApp.Auth.Jwt.Permissions.Users.Ban;
    }

    public static class Invoices
    {
        public const string View = MyApp.Auth.Jwt.Permissions.Invoices.View;
        public const string Edit = MyApp.Auth.Jwt.Permissions.Invoices.Edit;
        public const string Delete = MyApp.Auth.Jwt.Permissions.Invoices.Delete;
    }

    public static IReadOnlyList<string> All => MyApp.Auth.Jwt.Permissions.All;
}

public static class AuthClaims
{
    public const string Role = AuthClaimTypes.Role;
    public const string TokenVersion = AuthClaimTypes.TokenVersion;
    public const string Permission = AuthClaimTypes.Permission;
    public const string Amr = AuthClaimTypes.Amr;

    public static Guid GetUserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
