using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Users.Auth;
using MyApp.Modules.Users.Customers;

namespace MyApp.Modules.Users.Persistence;

/// <summary>
/// Users module DbContext (IAM + customers). SQLite file today; use
/// <c>HasDefaultSchema("users")</c> when moving to Postgres.
/// </summary>
public sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Customer> Customers => Set<Customer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // modelBuilder.HasDefaultSchema("users"); // enable with Postgres
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly);
        modelBuilder.Entity<Customer>(b =>
        {
            b.HasKey(c => c.Id);
            b.Property(c => c.Name).HasMaxLength(200).IsRequired();
            b.Property(c => c.Email).HasMaxLength(320).IsRequired();
            b.HasIndex(c => c.Email);
        });
    }
}
