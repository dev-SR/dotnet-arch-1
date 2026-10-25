using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.OrdersApi.Orders;

namespace MyApp.OrdersApi.Persistence.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> b)
    {
        b.HasKey(i => i.Id);
        b.Property(i => i.ProductName).HasMaxLength(200).IsRequired();
        b.Property(i => i.UnitPriceAtPurchase).HasPrecision(18, 2);
    }
}
