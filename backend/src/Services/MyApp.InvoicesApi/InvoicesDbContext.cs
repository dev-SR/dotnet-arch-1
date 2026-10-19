using Microsoft.EntityFrameworkCore;

namespace MyApp.InvoicesApi;

public sealed class InvoicesDbContext(DbContextOptions<InvoicesDbContext> options) : DbContext(options)
{
    public DbSet<Invoice> Invoices => Set<Invoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var b = modelBuilder.Entity<Invoice>();
        b.HasKey(x => x.Id);
        b.Property(x => x.Number).HasMaxLength(64).IsRequired();
        b.HasIndex(x => x.Number).IsUnique();
        b.Property(x => x.Amount).HasPrecision(18, 2);
    }
}
