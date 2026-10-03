using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Features.Auth.Specifications;

// Atomic Criteria filters — compose with .And() like ProductSpecs.
// Default AsNoTracking = true. Mutating handlers add ForUpdateSpecification<T>.Instance.
// ExecuteUpdate paths also .Apply(filter specs) — Criteria only, never Includes.

public sealed class UserByIdSpecification : SpecificationBase<User>
{
    public UserByIdSpecification(Guid userId) => Where(u => u.Id == userId);
}

public sealed class UserByNormalizedEmailSpecification : SpecificationBase<User>
{
    public UserByNormalizedEmailSpecification(string normalizedEmail) =>
        Where(u => u.NormalizedEmail == normalizedEmail);
}

public sealed class UserWithMinAccessFailedCountSpecification : SpecificationBase<User>
{
    public UserWithMinAccessFailedCountSpecification(int minFailed) =>
        Where(u => u.AccessFailedCount >= minFailed);
}

public sealed class RoleByIdSpecification : SpecificationBase<Role>
{
    public RoleByIdSpecification(Guid roleId) => Where(r => r.Id == roleId);
}

public sealed class RoleByNormalizedNameSpecification : SpecificationBase<Role>
{
    public RoleByNormalizedNameSpecification(string normalizedName) =>
        Where(r => r.NormalizedName == normalizedName);
}

public sealed class RefreshTokenByHashSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByHashSpecification(string tokenHash) =>
        Where(t => t.TokenHash == tokenHash);
}

public sealed class RefreshTokenByIdSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByIdSpecification(Guid id) => Where(t => t.Id == id);
}

public sealed class RefreshTokenByFamilyIdSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByFamilyIdSpecification(Guid familyId) =>
        Where(t => t.FamilyId == familyId);
}

public sealed class RefreshTokenByUserIdSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenByUserIdSpecification(Guid userId) =>
        Where(t => t.UserId == userId);
}

/// <summary>Not yet revoked — compose with id / family / user filters for ExecuteUpdate revoke.</summary>
public sealed class ActiveRefreshTokenSpecification : SpecificationBase<RefreshToken>
{
    public ActiveRefreshTokenSpecification() => Where(t => t.RevokedAt == null);

    public static readonly ISpecification<RefreshToken> Instance = new ActiveRefreshTokenSpecification();
}

public sealed class UserRolesByUserIdSpecification : SpecificationBase<UserRole>
{
    public UserRolesByUserIdSpecification(Guid userId) => Where(ur => ur.UserId == userId);
}

public sealed class UserRoleByUserAndRoleSpecification : SpecificationBase<UserRole>
{
    public UserRoleByUserAndRoleSpecification(Guid userId, Guid roleId) =>
        Where(ur => ur.UserId == userId && ur.RoleId == roleId);
}

// Include graphs only — no Criteria. And() with a filter above.

public sealed class RefreshTokenWithUserRolesSpecification : SpecificationBase<RefreshToken>
{
    public RefreshTokenWithUserRolesSpecification()
    {
        Include(q => q.Include(t => t.User)
            .ThenInclude(u => u!.UserRoles)
            .ThenInclude(ur => ur.Role));
    }

    public static readonly ISpecification<RefreshToken> Instance = new RefreshTokenWithUserRolesSpecification();
}
