using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.OrdersApi.Orders;

namespace MyApp.OrdersApi.Persistence.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> b)
    {
        b.HasKey(o => o.Id);
        b.Ignore(o => o.DomainEvents);
        b.Property(o => o.CustomerId).IsRequired();
        b.Property(o => o.Total).HasPrecision(18, 2);
        b.Property(o => o.CreatedBy).HasMaxLength(256);
        b.Property(o => o.LastModifiedBy).HasMaxLength(256);
        b.HasIndex(o => o.CustomerId);
        b.HasMany(o => o.Items).WithOne(i => i.Order).HasForeignKey(i => i.OrderId);
    }
}
