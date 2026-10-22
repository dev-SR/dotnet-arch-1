using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Orders.Catalog;

namespace MyApp.Modules.Orders.Persistence;

/// <summary>
/// Orders module DbContext. SQLite file today; use
/// <c>HasDefaultSchema("orders")</c> when moving to Postgres.
/// </summary>
public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<ProductCatalogItem> ProductCatalog => Set<ProductCatalogItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // modelBuilder.HasDefaultSchema("orders"); // enable with Postgres
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrdersDbContext).Assembly);
    }
}
