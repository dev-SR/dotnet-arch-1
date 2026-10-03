using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth;
using MyApp.Features.Brands;
using MyApp.Features.Categories;
using MyApp.Features.Customers;
using MyApp.Features.Orders;
using MyApp.Features.Products;
using MyApp.Features.Reviews;
using MyApp.Features.Tags;

namespace MyApp.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();


    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        // New: discover IEntityTypeConfiguration<> in this assembly.
        // Not present on today's AppDbContext — add it when you introduce the config classes above.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
