using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.LogoutAll;

internal sealed class LogoutAllCommandHandler(AppDbContext db, TimeProvider clock)
    : IRequestHandler<LogoutAllCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(LogoutAllCommand command, CancellationToken ct)
    {
        var user = await db.Users
            .Apply(new UserByIdSpecification(command.UserId)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null)
            return Error.NotFound("AUTH.USER_NOT_FOUND", "User was not found.");

        user.PermissionVersion++;
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Apply(new RefreshTokenByUserIdSpecification(user.Id)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        return default(Success);
    }
}
