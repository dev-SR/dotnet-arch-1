using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> b)
    {
        b.HasKey(x => new { x.RoleId, x.PermissionId });

        // User: Products.View only. Admin: all permissions.
        var userGrants = new[]
        {
            new RolePermission { RoleId = RoleConfiguration.UserRoleId, PermissionId = PermissionConfiguration.ProductsViewId },
        };

        var adminGrants = PermissionConfiguration.SeedRows
            .Select(p => new RolePermission
            {
                RoleId = RoleConfiguration.AdminRoleId,
                PermissionId = p.Id,
            })
            .ToArray();

        b.HasData(userGrants.Concat(adminGrants).ToArray());
    }
}
