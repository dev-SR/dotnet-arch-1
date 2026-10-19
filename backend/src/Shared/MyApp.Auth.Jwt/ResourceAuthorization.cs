using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.JsonWebTokens;

namespace MyApp.Auth.Jwt;

public interface IOwnedResource
{
    Guid OwnerId { get; }
}

public sealed class ResourcePermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }
    public bool RequireOwnership { get; }

    public ResourcePermissionRequirement(string permission, bool requireOwnership = true)
    {
        Permission = permission;
        RequireOwnership = requireOwnership;
    }
}

/// <summary>
/// JWT permission claim + optional ownership (sub == OwnerId). Admin role bypasses ownership.
/// </summary>
public sealed class ResourcePermissionHandler
    : AuthorizationHandler<ResourcePermissionRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourcePermissionRequirement requirement,
        IOwnedResource resource)
    {
        var hasPermission = context.User.HasClaim(c =>
            c.Type == AuthClaimTypes.Permission &&
            c.Value.Equals(requirement.Permission, StringComparison.OrdinalIgnoreCase));

        if (!hasPermission)
            return Task.CompletedTask;

        if (!requirement.RequireOwnership)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (context.User.IsInRole(Roles.Admin))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var sub = context.User.FindFirstValue(JwtRegisteredClaimNames.Sub);
        if (sub is not null
            && Guid.TryParse(sub, out var userId)
            && userId == resource.OwnerId)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
