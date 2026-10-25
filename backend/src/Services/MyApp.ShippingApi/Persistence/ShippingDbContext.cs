using Microsoft.EntityFrameworkCore;

namespace MyApp.ShippingApi.Persistence;

public sealed class ShippingDbContext(DbContextOptions<ShippingDbContext> options) : DbContext(options)
{
    public DbSet<Shipment> Shipments => Set<Shipment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Shipment>(b =>
        {
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.OrderId).IsUnique();
        });
    }
}
