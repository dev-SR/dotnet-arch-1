using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.Login;

internal sealed class LoginCommandHandler(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    ITokenService tokens,
    TimeProvider clock)
    : IRequestHandler<LoginCommand, ErrorOr<TokenResponse>>
{
    private const int MaxFailed = 5;
    private static readonly TimeSpan LockoutFor = TimeSpan.FromMinutes(15);

    private static readonly string DummyHash = new PasswordHasher<User>().HashPassword(
        new User { Email = "x", NormalizedEmail = "X", PasswordHash = "" },
        Guid.NewGuid().ToString());

    public async ValueTask<ErrorOr<TokenResponse>> Handle(LoginCommand command, CancellationToken ct)
    {
        var normalized = command.Email.Trim().ToUpperInvariant();
        var user = await db.Users
            .Apply(new UserByNormalizedEmailSpecification(normalized)
                .And(ForUpdateSpecification<User>.Instance))
            .SingleOrDefaultAsync(ct);

        if (user is null || user.IsBanned)
        {
            BurnPasswordVerify(command.Password);
            return Error.Unauthorized("AUTH.INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (user.LockoutEnd is { } until && until > clock.GetUtcNow())
        {
            BurnPasswordVerify(command.Password);
            return Error.Unauthorized("AUTH.INVALID_CREDENTIALS", "Invalid credentials.");
        }

        var verify = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, command.Password);
        if (verify == PasswordVerificationResult.Failed)
        {
            await db.Users
                .Apply(new UserByIdSpecification(user.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.AccessFailedCount, u => u.AccessFailedCount + 1), ct);

            var lockUntil = clock.GetUtcNow().Add(LockoutFor);
            await db.Users
                .Apply(new UserByIdSpecification(user.Id)
                    .And(new UserWithMinAccessFailedCountSpecification(MaxFailed)))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.LockoutEnd, lockUntil)
                    .SetProperty(u => u.AccessFailedCount, 0), ct);

            return Error.Unauthorized("AUTH.INVALID_CREDENTIALS", "Invalid credentials.");
        }

        if (verify == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, command.Password);

        user.AccessFailedCount = 0;
        user.LockoutEnd = null;

        var roles = await db.UserRoles
            .Apply(new UserRolesByUserIdSpecification(user.Id))
            .Select(ur => ur.Role.Name)
            .ToListAsync(ct);

        var issued = tokens.IssueTokens(user, roles, Guid.NewGuid());
        db.RefreshTokens.Add(issued.RefreshToken);
        return new TokenResponse(
            issued.AccessToken,
            "Bearer",
            issued.AccessTokenExpiresInSeconds,
            issued.RawRefreshToken);
    }

    private void BurnPasswordVerify(string password) =>
        passwordHasher.VerifyHashedPassword(
            new User { Email = "x", NormalizedEmail = "X", PasswordHash = DummyHash },
            DummyHash,
            password);
}
