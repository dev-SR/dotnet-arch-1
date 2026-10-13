using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;

namespace MyApp.Features.Auth.AdminUsers;

public sealed class AdminPermissionsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/auth/admin/roles/{roleId:guid}/permissions")
            .WithTags("AuthAdmin")
            .RequireAuthorization(Policies.AdminOnly);

        group.MapPost("/", AssignAsync).WithName("AssignRolePermission");
        group.MapDelete("/", RemoveAsync).WithName("RemoveRolePermission");
        group.MapGet("/", ListAsync).WithName("ListRolePermissions");
    }

    private static async Task<IResult> AssignAsync(
        Guid roleId,
        AssignPermissionBody body,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new AssignPermissionCommand(roleId, body.Permission), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> RemoveAsync(
        Guid roleId,
        string permission,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new RemovePermissionCommand(roleId, permission), ct);
        return result.MatchNoContent(http);
    }

    private static async Task<IResult> ListAsync(
        Guid roleId,
        AppDbContext db,
        CancellationToken ct)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == roleId, ct))
            return Results.NotFound();

        var names = await db.RolePermissions
            .Where(rp => rp.RoleId == roleId)
            .Select(rp => rp.Permission.Name)
            .OrderBy(n => n)
            .ToListAsync(ct);

        return Results.Ok(names);
    }

    public sealed record AssignPermissionBody(string Permission);
}
