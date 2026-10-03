using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.AdminUsers;

internal sealed class BanUserCommandHandler(AppDbContext db, TimeProvider clock)
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

internal sealed class UnbanUserCommandHandler(AppDbContext db)
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
