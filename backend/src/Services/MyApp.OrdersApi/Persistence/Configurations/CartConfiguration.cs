using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.OrdersApi.Cart;

namespace MyApp.OrdersApi.Persistence.Configurations;

public sealed class CartConfiguration : IEntityTypeConfiguration<Cart.Cart>
{
    public void Configure(EntityTypeBuilder<Cart.Cart> b)
    {
        b.ToTable("carts");
        b.HasKey(c => c.CustomerId);
        b.HasMany(c => c.Items).WithOne(i => i.Cart).HasForeignKey(i => i.CustomerId);
    }
}

public sealed class CartItemConfiguration : IEntityTypeConfiguration<CartItem>
{
    public void Configure(EntityTypeBuilder<CartItem> b)
    {
        b.ToTable("cart_items");
        b.HasKey(i => i.Id);
        b.HasIndex(i => new { i.CustomerId, i.ProductId }).IsUnique();
    }
}
