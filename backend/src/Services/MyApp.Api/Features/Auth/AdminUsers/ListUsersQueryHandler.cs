using Microsoft.EntityFrameworkCore;
using MyApp.Persistence;

namespace MyApp.Features.Auth.AdminUsers;

internal sealed class ListUsersQueryHandler(AppDbContext db)
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
