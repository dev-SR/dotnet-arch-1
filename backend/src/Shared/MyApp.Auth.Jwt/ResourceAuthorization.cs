using Microsoft.AspNetCore.Authorization;

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
/// The rule itself lives in <see cref="ResourceAccess"/>.
/// </summary>
public sealed class ResourcePermissionHandler
    : AuthorizationHandler<ResourcePermissionRequirement, IOwnedResource>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ResourcePermissionRequirement requirement,
        IOwnedResource resource)
    {
        var caller = Caller.From(context.User);
        if (ResourceAccess.IsAllowed(caller, resource.OwnerId, requirement.Permission, requirement.RequireOwnership))
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
