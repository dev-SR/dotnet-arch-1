using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyApp.Features.Invoices;

namespace MyApp.Persistence.Configurations;

public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Amount).HasPrecision(18, 2);
    }
}
