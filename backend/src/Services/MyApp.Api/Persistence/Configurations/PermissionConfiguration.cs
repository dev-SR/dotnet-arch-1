using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Auth;

namespace MyApp.Persistence.Configurations;

public sealed class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    // Stable IDs for HasData + RolePermission seed.
    public static readonly Guid ProductsViewId = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    public static readonly Guid ProductsCreateId = Guid.Parse("a1111111-1111-1111-1111-111111111112");
    public static readonly Guid ProductsEditId = Guid.Parse("a1111111-1111-1111-1111-111111111113");
    public static readonly Guid ProductsDeleteId = Guid.Parse("a1111111-1111-1111-1111-111111111114");
    public static readonly Guid UsersViewId = Guid.Parse("a2222222-2222-2222-2222-222222222221");
    public static readonly Guid UsersBanId = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    public static readonly Guid InvoicesViewId = Guid.Parse("a3333333-3333-3333-3333-333333333331");
    public static readonly Guid InvoicesEditId = Guid.Parse("a3333333-3333-3333-3333-333333333332");
    public static readonly Guid InvoicesDeleteId = Guid.Parse("a3333333-3333-3333-3333-333333333333");

    public static IReadOnlyList<(Guid Id, string Name)> SeedRows { get; } =
    [
        (ProductsViewId, Permissions.Products.View),
        (ProductsCreateId, Permissions.Products.Create),
        (ProductsEditId, Permissions.Products.Edit),
        (ProductsDeleteId, Permissions.Products.Delete),
        (UsersViewId, Permissions.Users.View),
        (UsersBanId, Permissions.Users.Ban),
        (InvoicesViewId, Permissions.Invoices.View),
        (InvoicesEditId, Permissions.Invoices.Edit),
        (InvoicesDeleteId, Permissions.Invoices.Delete),
    ];

    public void Configure(EntityTypeBuilder<Permission> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(128).IsRequired();
        b.HasIndex(x => x.Name).IsUnique();
        b.HasMany(x => x.RolePermissions).WithOne(x => x.Permission).HasForeignKey(x => x.PermissionId);

        b.HasData(SeedRows.Select(r => new Permission { Id = r.Id, Name = r.Name }).ToArray());
    }
}
