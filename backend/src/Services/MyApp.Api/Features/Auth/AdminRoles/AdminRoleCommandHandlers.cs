using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using MyApp.Persistence.Configurations;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.AdminRoles;

internal sealed class ListRolesQueryHandler(AppDbContext db)
    : IRequestHandler<ListRolesQuery, ErrorOr<IReadOnlyList<RoleListItem>>>
{
    public async ValueTask<ErrorOr<IReadOnlyList<RoleListItem>>> Handle(
        ListRolesQuery query, CancellationToken ct)
    {
        var roles = await db.Roles
            .AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new RoleListItem(r.Id, r.Name, r.NormalizedName))
            .ToListAsync(ct);

        return roles;
    }
}

internal sealed class CreateRoleCommandHandler(AppDbContext db)
    : IRequestHandler<CreateRoleCommand, ErrorOr<RoleListItem>>
{
    public async ValueTask<ErrorOr<RoleListItem>> Handle(CreateRoleCommand command, CancellationToken ct)
    {
        var name = command.Name.Trim();
        var normalized = name.ToUpperInvariant();

        if (await db.Roles
                .Apply(new RoleByNormalizedNameSpecification(normalized))
                .AnyAsync(ct))
            return Error.Conflict("AUTH.ROLE_TAKEN", "A role with that name already exists.");

        var role = new Role { Name = name, NormalizedName = normalized };
        db.Roles.Add(role);
        return new RoleListItem(role.Id, role.Name, role.NormalizedName);
    }
}

internal sealed class RenameRoleCommandHandler(AppDbContext db)
    : IRequestHandler<RenameRoleCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(RenameRoleCommand command, CancellationToken ct)
    {
        if (SeededRoles.IsSeededRole(command.RoleId))
            return Error.Forbidden("AUTH.ROLE_PROTECTED", "Seeded roles cannot be renamed.");

        var role = await db.Roles
            .Apply(new RoleByIdSpecification(command.RoleId)
                .And(ForUpdateSpecification<Role>.Instance))
            .SingleOrDefaultAsync(ct);

        if (role is null)
            return Error.NotFound("AUTH.ROLE_NOT_FOUND", "Role was not found.");

        var name = command.Name.Trim();
        var normalized = name.ToUpperInvariant();

        if (await db.Roles
                .Where(r => r.NormalizedName == normalized && r.Id != role.Id)
                .AnyAsync(ct))
            return Error.Conflict("AUTH.ROLE_TAKEN", "A role with that name already exists.");

        role.Name = name;
        role.NormalizedName = normalized;
        return default(Success);
    }
}

internal sealed class DeleteRoleCommandHandler(AppDbContext db)
    : IRequestHandler<DeleteRoleCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(DeleteRoleCommand command, CancellationToken ct)
    {
        if (SeededRoles.IsSeededRole(command.RoleId))
            return Error.Forbidden("AUTH.ROLE_PROTECTED", "Seeded roles cannot be deleted.");

        var role = await db.Roles
            .Apply(new RoleByIdSpecification(command.RoleId)
                .And(ForUpdateSpecification<Role>.Instance))
            .SingleOrDefaultAsync(ct);

        if (role is null)
            return Error.NotFound("AUTH.ROLE_NOT_FOUND", "Role was not found.");

        var memberCount = await db.UserRoles.CountAsync(ur => ur.RoleId == role.Id, ct);
        if (memberCount > 0)
            return Error.Conflict("AUTH.ROLE_IN_USE", "Remove the role from all users before deleting it.");

        db.Roles.Remove(role);
        return default(Success);
    }
}

file static class SeededRoles
{
    public static bool IsSeededRole(Guid roleId) =>
        roleId == RoleConfiguration.UserRoleId || roleId == RoleConfiguration.AdminRoleId;
}
