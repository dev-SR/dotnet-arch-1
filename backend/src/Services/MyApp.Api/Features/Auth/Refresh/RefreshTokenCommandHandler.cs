using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.Refresh;

internal sealed class RefreshTokenCommandHandler(
    AppDbContext db,
    ITokenService tokens,
    TimeProvider clock)
    : IRequestHandler<RefreshTokenCommand, ErrorOr<TokenResponse>>
{
    public async ValueTask<ErrorOr<TokenResponse>> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        var hash = tokens.Hash(command.RefreshToken);
        var stored = await db.RefreshTokens
            .Apply(new RefreshTokenByHashSpecification(hash)
                .And(RefreshTokenWithUserRolesSpecification.Instance))
            .SingleOrDefaultAsync(ct);

        var now = clock.GetUtcNow().UtcDateTime;

        if (stored is null)
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");

        // Reuse: already-rotated token presented → kill the whole family.
        if (stored.RevokedAt is not null)
        {
            await db.RefreshTokens
                .Apply(new RefreshTokenByFamilyIdSpecification(stored.FamilyId)
                    .And(ActiveRefreshTokenSpecification.Instance))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");
        }

        if (stored.ExpiresAt <= now)
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");

        if (stored.User is null || stored.User.IsBanned)
            return Error.Forbidden("AUTH.ACCOUNT_DISABLED", "Account is locked or disabled.");

        var revoked = await db.RefreshTokens
            .Apply(new RefreshTokenByIdSpecification(stored.Id)
                .And(ActiveRefreshTokenSpecification.Instance))
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);

        if (revoked == 0)
        {
            await db.RefreshTokens
                .Apply(new RefreshTokenByFamilyIdSpecification(stored.FamilyId)
                    .And(ActiveRefreshTokenSpecification.Instance))
                .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), ct);
            return Error.Unauthorized("AUTH.INVALID_TOKEN", "Invalid refresh token.");
        }

        var roles = stored.User.UserRoles.Select(ur => ur.Role.Name);
        var issued = tokens.IssueTokens(stored.User, roles, stored.FamilyId);
        db.RefreshTokens.Add(issued.RefreshToken);
        return new TokenResponse(
            issued.AccessToken,
            "Bearer",
            issued.AccessTokenExpiresInSeconds,
            issued.RawRefreshToken);
    }
}
