using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Products;

namespace MyApp.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(p => p.Name).HasMaxLength(200).IsRequired();
        b.Property(p => p.Price).HasPrecision(18, 2);

        b.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId);
        b.HasOne(p => p.Brand).WithMany(br => br.Products).HasForeignKey(p => p.BrandId);

        // Skip navigation both sides — EF Core creates and manages a "ProductTag" join
        // table with no corresponding C# class. Query it via p.Tags / t.Products directly.
        b.HasMany(p => p.Tags).WithMany(t => t.Products);

        // Indexes for exactly the columns the filtering/sorting section (3) actually
        // uses — see the "why" in that section for which ones matter and why.
        b.HasIndex(p => p.CategoryId);
        b.HasIndex(p => p.BrandId);
        b.HasIndex(p => p.Price);
        b.HasIndex(p => new { p.IsActive, p.IsDeleted });
    }
}
