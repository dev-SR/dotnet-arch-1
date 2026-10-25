using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Users.Persistence;
using Shared.Common.Errors;

namespace MyApp.Modules.Users.Auth.AdminUsers;

internal sealed class ListRolePermissionsQueryHandler(UsersDbContext db)
    : IRequestHandler<ListRolePermissionsQuery, ErrorOr<IReadOnlyList<string>>>
{
    public async ValueTask<ErrorOr<IReadOnlyList<string>>> Handle(
        ListRolePermissionsQuery query,
        CancellationToken cancellationToken)
    {
        if (!await db.Roles.AnyAsync(r => r.Id == query.RoleId, cancellationToken))
            return Error.NotFound("ROLE.NOT_FOUND", $"Role '{query.RoleId}' was not found.");

        var names = await db.RolePermissions
            .AsNoTracking()
            .Where(rp => rp.RoleId == query.RoleId)
            .Select(rp => rp.Permission.Name)
            .OrderBy(n => n)
            .ToListAsync(cancellationToken);

        return names;
    }
}
