using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Users.Auth.Specifications;
using MyApp.Modules.Users.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Modules.Users.Auth.Logout;

internal sealed class LogoutCommandHandler(UsersDbContext db, ITokenService tokens, TimeProvider clock)
    : IRequestHandler<LogoutCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(LogoutCommand command, CancellationToken ct)
    {
        var hash = tokens.Hash(command.RefreshToken);
        var now = clock.GetUtcNow().UtcDateTime;

        var familyId = await db.RefreshTokens
            .Apply(new RefreshTokenByHashSpecification(hash))
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

        if (familyId is null)
            return default(Success);

        await db.RefreshTokens
            .Apply(new RefreshTokenByFamilyIdSpecification(familyId.Value)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        return default(Success);
    }
}
