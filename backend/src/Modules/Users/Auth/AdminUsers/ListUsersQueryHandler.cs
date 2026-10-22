using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Users.Persistence;

namespace MyApp.Modules.Users.Auth.AdminUsers;

internal sealed class ListUsersQueryHandler(UsersDbContext db)
    : IRequestHandler<ListUsersQuery, ErrorOr<IReadOnlyList<UserListItem>>>
{
    public async ValueTask<ErrorOr<IReadOnlyList<UserListItem>>> Handle(
        ListUsersQuery query, CancellationToken ct)
    {
        var users = await db.Users
            .AsNoTracking()
            .OrderBy(u => u.Email)
            .Select(u => new UserListItem(u.Id, u.Email, u.IsBanned, u.PermissionVersion))
            .ToListAsync(ct);

        return users;
    }
}
