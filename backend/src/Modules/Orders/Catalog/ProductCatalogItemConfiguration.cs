using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyApp.Modules.Orders.Catalog;

public sealed class ProductCatalogItemConfiguration : IEntityTypeConfiguration<ProductCatalogItem>
{
    public void Configure(EntityTypeBuilder<ProductCatalogItem> builder)
    {
        builder.ToTable("product_catalog");
        builder.HasKey(x => x.ProductId);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Price).HasPrecision(18, 2);
    }
}
