namespace MyApp.Features.Auth;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string PasswordHash { get; set; }
    public bool IsBanned { get; set; }
    /// <summary>Bumped on ban / sign-out-everywhere. JWT claim <c>token_version</c>.</summary>
    public int PermissionVersion { get; set; } = 1;
    public int AccessFailedCount { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public List<UserRole> UserRoles { get; set; } = [];
    public List<RefreshToken> RefreshTokens { get; set; } = [];
}

public sealed class Role
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string NormalizedName { get; set; }
    public List<UserRole> UserRoles { get; set; } = [];
}

public sealed class UserRole
{
    public Guid UserId { get; init; }
    public User User { get; set; } = null!;
    public Guid RoleId { get; init; }
    public Role Role { get; set; } = null!;
}

public sealed class RefreshToken
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid UserId { get; init; }
    public User User { get; set; } = null!;
    public required string TokenHash { get; init; }
    public Guid FamilyId { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime ExpiresAt { get; init; }
    public DateTime? RevokedAt { get; set; }
}
