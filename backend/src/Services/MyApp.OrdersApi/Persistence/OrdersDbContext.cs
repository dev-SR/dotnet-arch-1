using Microsoft.EntityFrameworkCore;
using MyApp.OrdersApi.Cart;
using MyApp.OrdersApi.Catalog;
using MyApp.OrdersApi.Orders;

namespace MyApp.OrdersApi.Persistence;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ProductCatalogItem> ProductCatalog => Set<ProductCatalogItem>();
    public DbSet<Cart.Cart> Carts => Set<Cart.Cart>();
    public DbSet<CartItem> CartItems => Set<CartItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}
