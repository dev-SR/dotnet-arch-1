using Microsoft.EntityFrameworkCore;
using MyApp.Features.Products;

namespace MyApp.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Name).IsRequired().HasMaxLength(100);
            entity.Property(p => p.Category).IsRequired().HasMaxLength(50);

            // Note: SQLite doesn't have a native 'decimal' type.
            // EF Core automatically maps decimal to TEXT in SQLite to preserve exact precision.
        });
    }
}
