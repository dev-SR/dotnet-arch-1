using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.AdminRoles;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.AdminUsers;

internal sealed class ListUserRolesQueryHandler(AppDbContext db)
    : IRequestHandler<ListUserRolesQuery, ErrorOr<IReadOnlyList<RoleListItem>>>
{
    public async ValueTask<ErrorOr<IReadOnlyList<RoleListItem>>> Handle(
        ListUserRolesQuery query, CancellationToken ct)
    {
        if (!await db.Users.Apply(new UserByIdSpecification(query.UserId)).AnyAsync(ct))
            return Error.NotFound("AUTH.USER_NOT_FOUND", "User was not found.");

        var roles = await db.UserRoles
            .AsNoTracking()
            .Where(ur => ur.UserId == query.UserId)
            .Select(ur => new RoleListItem(ur.Role.Id, ur.Role.Name, ur.Role.NormalizedName))
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        return roles;
    }
}

internal sealed class AssignRoleCommandHandler(AppDbContext db, TimeProvider clock)
    : IRequestHandler<AssignRoleCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(AssignRoleCommand command, CancellationToken ct)
    {
        var user = await db.Users
            .Apply(new UserByIdSpecification(command.UserId)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return Error.NotFound("AUTH.USER_NOT_FOUND", "User was not found.");

        if (!await db.Roles.Apply(new RoleByIdSpecification(command.RoleId)).AnyAsync(ct))
            return Error.NotFound("AUTH.ROLE_NOT_FOUND", "Role was not found.");

        if (await db.UserRoles
                .Apply(new UserRoleByUserAndRoleSpecification(command.UserId, command.RoleId))
                .AnyAsync(ct))
            return Error.Conflict("AUTH.ROLE_ALREADY_ASSIGNED", "User already has this role.");

        db.UserRoles.Add(new UserRole { UserId = command.UserId, RoleId = command.RoleId });
        user.PermissionVersion++;

        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Apply(new RefreshTokenByUserIdSpecification(user.Id)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        return default(Success);
    }
}

internal sealed class RemoveRoleCommandHandler(AppDbContext db, TimeProvider clock)
    : IRequestHandler<RemoveRoleCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(RemoveRoleCommand command, CancellationToken ct)
    {
        var user = await db.Users
            .Apply(new UserByIdSpecification(command.UserId)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return Error.NotFound("AUTH.USER_NOT_FOUND", "User was not found.");

        var membership = await db.UserRoles
            .Apply(new UserRoleByUserAndRoleSpecification(command.UserId, command.RoleId))
            .SingleOrDefaultAsync(ct);

        if (membership is null)
            return Error.NotFound("AUTH.ROLE_NOT_ASSIGNED", "User does not have this role.");

        db.UserRoles.Remove(membership);
        user.PermissionVersion++;

        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Apply(new RefreshTokenByUserIdSpecification(user.Id)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        return default(Success);
    }
}
