using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Users.Auth.Specifications;
using MyApp.Modules.Users.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Modules.Users.Auth.AdminUsers;

internal sealed class BanUserCommandHandler(UsersDbContext db, TimeProvider clock)
    : IRequestHandler<BanUserCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(BanUserCommand command, CancellationToken ct)
    {
        if (command.UserId == command.CallerId)
            return Error.Forbidden("AUTH.FORBIDDEN", "You cannot ban yourself.");

        var user = await db.Users
            .Apply(new UserByIdSpecification(command.UserId)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return Error.NotFound("AUTH.USER_NOT_FOUND", "User was not found.");

        user.IsBanned = true;
        user.PermissionVersion++;

        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Apply(new RefreshTokenByUserIdSpecification(user.Id)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        return default(Success);
    }
}

internal sealed class UnbanUserCommandHandler(UsersDbContext db)
    : IRequestHandler<UnbanUserCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(UnbanUserCommand command, CancellationToken ct)
    {
        var user = await db.Users
            .Apply(new UserByIdSpecification(command.UserId)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return Error.NotFound("AUTH.USER_NOT_FOUND", "User was not found.");

        user.IsBanned = false;
        return default(Success);
    }
}
