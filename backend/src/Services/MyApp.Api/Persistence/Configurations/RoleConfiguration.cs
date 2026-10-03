using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    // Fixed GUIDs so HasData is stable across environments.
    public static readonly Guid UserRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid AdminRoleId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public void Configure(EntityTypeBuilder<Role> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(64).IsRequired();
        b.Property(x => x.NormalizedName).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.NormalizedName).IsUnique();
        b.HasMany(x => x.UserRoles).WithOne(x => x.Role).HasForeignKey(x => x.RoleId);

        b.HasData(
            new Role { Id = UserRoleId, Name = Roles.User, NormalizedName = "USER" },
            new Role { Id = AdminRoleId, Name = Roles.Admin, NormalizedName = "ADMIN" });
    }
}
