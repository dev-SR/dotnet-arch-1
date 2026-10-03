using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Email).HasMaxLength(256).IsRequired();
        b.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        b.Property(x => x.PasswordHash).IsRequired();
        b.HasIndex(x => x.NormalizedEmail).IsUnique();
        b.HasMany(x => x.UserRoles).WithOne(x => x.User).HasForeignKey(x => x.UserId);
        // RefreshTokens relationship configured only in RefreshTokenConfiguration
    }
}
