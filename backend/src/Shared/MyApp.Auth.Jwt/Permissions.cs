using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MyApp.Auth.Jwt;

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

/// <summary>Permission strings — also used as ASP.NET policy names via PermissionPolicyProvider.</summary>
public static class Permissions
{
    public const string Prefix = "Permissions.";

    public static class Products
    {
        public const string View = "Permissions.Products.View";
        public const string Create = "Permissions.Products.Create";
        public const string Edit = "Permissions.Products.Edit";
        public const string Delete = "Permissions.Products.Delete";
    }

    public static class Users
    {
        public const string View = "Permissions.Users.View";
        public const string Ban = "Permissions.Users.Ban";
    }

    public static class Invoices
    {
        public const string View = "Permissions.Invoices.View";
        public const string Edit = "Permissions.Invoices.Edit";
        public const string Delete = "Permissions.Invoices.Delete";
    }

    public static IReadOnlyList<string> All { get; } =
    [
        Products.View, Products.Create, Products.Edit, Products.Delete,
        Users.View, Users.Ban,
        Invoices.View, Invoices.Edit, Invoices.Delete,
    ];
}

public static class AuthClaimTypes
{
    public const string Role = "role";
    public const string TokenVersion = "token_version";
    public const string Permission = "permission";
    public const string Amr = "amr";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal p) =>
        Guid.Parse(p.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
}
