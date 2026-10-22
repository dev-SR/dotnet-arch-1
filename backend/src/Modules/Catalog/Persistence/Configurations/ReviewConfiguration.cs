using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Modules.Catalog.Reviews;

namespace MyApp.Modules.Catalog.Persistence.Configurations;

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> b)
    {
        b.HasOne(r => r.Product).WithMany(p => p.Reviews).HasForeignKey(r => r.ProductId);
        b.Property(r => r.CustomerId).IsRequired();
        b.HasIndex(r => r.ProductId);
        b.HasIndex(r => r.CustomerId);
    }
}
