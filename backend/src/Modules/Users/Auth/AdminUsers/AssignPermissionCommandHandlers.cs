using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Users.Auth.Specifications;
using MyApp.Modules.Users.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Modules.Users.Auth.AdminUsers;

internal sealed class AssignPermissionCommandHandler(UsersDbContext db, TimeProvider clock)
    : IRequestHandler<AssignPermissionCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(AssignPermissionCommand command, CancellationToken ct)
    {
        if (!await db.Roles.Apply(new RoleByIdSpecification(command.RoleId)).AnyAsync(ct))
            return Error.NotFound("AUTH.ROLE_NOT_FOUND", "Role was not found.");

        var permission = await db.Permissions
            .Apply(new PermissionByNameSpecification(command.Permission))
            .SingleOrDefaultAsync(ct);

        if (permission is null)
            return Error.NotFound("AUTH.PERMISSION_NOT_FOUND", "Permission was not found.");

        if (await db.RolePermissions
                .Apply(new RolePermissionByRoleAndPermissionSpecification(command.RoleId, permission.Id))
                .AnyAsync(ct))
            return Error.Conflict("AUTH.PERMISSION_ALREADY_ASSIGNED", "Role already has this permission.");

        db.RolePermissions.Add(new RolePermission
        {
            RoleId = command.RoleId,
            PermissionId = permission.Id,
        });

        await InvalidateUsersInRoleAsync(command.RoleId, clock, db, ct);
        return default(Success);
    }

    internal static async Task InvalidateUsersInRoleAsync(
        Guid roleId, TimeProvider clock, UsersDbContext db, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var userIds = await db.UserRoles
            .Where(ur => ur.RoleId == roleId)
            .Select(ur => ur.UserId)
            .ToListAsync(ct);

        if (userIds.Count == 0)
            return;

        await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.PermissionVersion, u => u.PermissionVersion + 1), ct);

        await db.RefreshTokens
            .Where(t => userIds.Contains(t.UserId) && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
    }
}

internal sealed class RemovePermissionCommandHandler(UsersDbContext db, TimeProvider clock)
    : IRequestHandler<RemovePermissionCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(RemovePermissionCommand command, CancellationToken ct)
    {
        var permission = await db.Permissions
            .Apply(new PermissionByNameSpecification(command.Permission))
            .SingleOrDefaultAsync(ct);

        if (permission is null)
            return Error.NotFound("AUTH.PERMISSION_NOT_FOUND", "Permission was not found.");

        var link = await db.RolePermissions
            .Apply(new RolePermissionByRoleAndPermissionSpecification(command.RoleId, permission.Id))
            .SingleOrDefaultAsync(ct);

        if (link is null)
            return Error.NotFound("AUTH.PERMISSION_NOT_ASSIGNED", "Role does not have this permission.");

        db.RolePermissions.Remove(link);
        await AssignPermissionCommandHandler.InvalidateUsersInRoleAsync(command.RoleId, clock, db, ct);
        return default(Success);
    }
}
