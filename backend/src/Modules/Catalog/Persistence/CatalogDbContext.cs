using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Products;
using MyApp.Modules.Catalog.Reviews;
using MyApp.Modules.Catalog.Tags;

namespace MyApp.Modules.Catalog.Persistence;

/// <summary>
/// Catalog module DbContext. SQLite file today; use
/// <c>HasDefaultSchema("catalog")</c> when moving to Postgres.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Review> Reviews => Set<Review>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // modelBuilder.HasDefaultSchema("catalog"); // enable with Postgres
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
    }
}
