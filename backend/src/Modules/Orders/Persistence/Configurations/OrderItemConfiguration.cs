using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MyApp.Modules.Orders.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.HasKey(i => i.Id);
        b.Property(i => i.UnitPriceAtPurchase).HasPrecision(18, 2);
        b.Property(i => i.ProductId).IsRequired();
        b.HasIndex(i => i.ProductId);
    }
}
