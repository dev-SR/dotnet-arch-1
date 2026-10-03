using Bogus;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MyApp.Config;
using MyApp.Features.Auth;
using MyApp.Features.Brands;
using MyApp.Features.Categories;
using MyApp.Features.Customers;
using MyApp.Features.Orders;
using MyApp.Features.Products;
using MyApp.Features.Reviews;
using MyApp.Features.Tags;
using MyApp.Persistence.Configurations;

namespace MyApp.Persistence.Seeding;

public static class DataSeeder
{
public static async Task SeedAsync(
        AppDbContext db,
        SeedOptions options,
        IPasswordHasher<User> passwordHasher,
        CancellationToken ct = default)
    {
        await SeedAdminAsync(db, options, passwordHasher, ct);
        await SeedUserAsync(db, options, passwordHasher, ct);
        await SeedProductModuleAsync(db, ct);
    }

    private static async Task SeedAdminAsync(
        AppDbContext db,
        SeedOptions options,
        IPasswordHasher<User> passwordHasher,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.AdminEmail) ||
            string.IsNullOrWhiteSpace(options.AdminPassword))
        {
            return;
        }

        var email = options.AdminEmail.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        var existing = await db.Users
            .FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalizedEmail,
                ct);

        if (existing is not null)
            return;

        var user = new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty
        };

        user.PasswordHash = passwordHasher.HashPassword(
            user,
            options.AdminPassword);

        db.Users.Add(user);

        db.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = RoleConfiguration.AdminRoleId
        });

        db.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = RoleConfiguration.UserRoleId
        });

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedUserAsync(
        AppDbContext db,
        SeedOptions options,
        IPasswordHasher<User> passwordHasher,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.UserEmail) ||
            string.IsNullOrWhiteSpace(options.UserPassword))
        {
            return;
        }

        var email = options.UserEmail.Trim();
        var normalizedEmail = email.ToUpperInvariant();

        var existing = await db.Users
            .FirstOrDefaultAsync(
                u => u.NormalizedEmail == normalizedEmail,
                ct);

        if (existing is not null)
            return;

        var user = new User
        {
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = string.Empty
        };

        user.PasswordHash = passwordHasher.HashPassword(
            user,
            options.UserPassword);

        db.Users.Add(user);

        db.UserRoles.Add(new UserRole
        {
            UserId = user.Id,
            RoleId = RoleConfiguration.UserRoleId
        });

        await db.SaveChangesAsync(ct);
    }
    private  static async Task SeedProductModuleAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(ct)) return;   // idempotent — see the "why" above

        var random = new Random(20260927);       // fixed seed: reruns produce the same dataset
        Randomizer.Seed = random;

        var categories = new Faker<Category>()
            .RuleFor(c => c.Id, f => f.Random.Guid())
            .RuleFor(c => c.Name, f => f.Commerce.Categories(1)[0])
            .Generate(8)
            .DistinctBy(c => c.Name)
            .ToList();

        var brands = new Faker<Brand>()
            .RuleFor(b => b.Id, f => f.Random.Guid())
            .RuleFor(b => b.Name, f => f.Company.CompanyName())
            .RuleFor(b => b.Country, f => f.Address.Country())
            .Generate(10);

        var tags = new Faker<Tag>()
            .RuleFor(t => t.Id, f => f.Random.Guid())
            .RuleFor(t => t.Name, f => f.Commerce.ProductAdjective())
            .Generate(20)
            .DistinctBy(t => t.Name)
            .ToList();

        var customers = new Faker<Customer>()
            .RuleFor(c => c.Id, f => f.Random.Guid())
            .RuleFor(c => c.Name, f => f.Name.FullName())
            .RuleFor(c => c.Email, (f, c) => f.Internet.Email(c.Name))
            .Generate(100);

        var productFaker = new Faker<Product>()
            .RuleFor(p => p.Id, f => f.Random.Guid())
            .RuleFor(p => p.Name, f => f.Commerce.ProductName())
            .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
            .RuleFor(p => p.Price, f => f.Random.Decimal(5, 2000))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(0, 500))
            .RuleFor(p => p.IsActive, f => f.Random.Bool(0.9f))     // 90% active — some inactive rows to filter out
            .RuleFor(p => p.IsDeleted, f => f.Random.Bool(0.03f))   // a few soft-deleted rows
            .RuleFor(p => p.CreatedAt, f => f.Date.Past(2))
            .RuleFor(p => p.CategoryId, f => f.PickRandom(categories).Id)
            .RuleFor(p => p.BrandId, f => f.PickRandom(brands).Id);

        var products = productFaker.Generate(600);

        // Assign 1–4 random tags per product AFTER generation (skip-navigation collections
        // aren't settable via RuleFor the same way scalar columns are).
        foreach (var p in products)
            p.Tags = tags.OrderBy(_ => random.Next()).Take(random.Next(1, 5)).ToList();

        var reviews = new List<Review>();
        var reviewFaker = new Faker<Review>()
            .RuleFor(r => r.Id, f => f.Random.Guid())
            .RuleFor(r => r.Rating, f => f.Random.Int(1, 5))
            .RuleFor(r => r.Comment, f => f.Random.Bool(0.7f) ? f.Rant.Review() : null)
            .RuleFor(r => r.CreatedAt, f => f.Date.Past(1));

        // Roughly 0–6 reviews per product, each from a random customer — building this by
        // hand (rather than one flat Faker<Review>().Generate(N)) is what lets every
        // review point at a product/customer that actually exists.
        foreach (var product in products)
        {
            var reviewCount = random.Next(0, 7);
            for (var i = 0; i < reviewCount; i++)
            {
                var review = reviewFaker.Generate();
                review.ProductId = product.Id;
                review.CustomerId = customers[random.Next(customers.Count)].Id;
                reviews.Add(review);
            }
        }

        var orders = new List<Order>();
        var orderItems = new List<OrderItem>();
        var activeProducts = products.Where(p => p.IsActive && !p.IsDeleted).ToList();

        foreach (var customer in customers)
        {
            var orderCount = random.Next(0, 4);   // some customers have no orders at all
            for (var i = 0; i < orderCount; i++)
            {
                var order = new Order
                {
                    Id = Guid.NewGuid(),
                    CustomerId = customer.Id,
                    Status = (OrderStatus)random.Next(0, 4),
                    CreatedAt = DateTime.UtcNow.AddDays(-random.Next(1, 365)),
                };
                orders.Add(order);

                var itemCount = random.Next(1, 5);
                foreach (var chosen in activeProducts.OrderBy(_ => random.Next()).Take(itemCount))
                {
                    orderItems.Add(new OrderItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        ProductId = chosen.Id,
                        Quantity = random.Next(1, 4),
                        UnitPriceAtPurchase = chosen.Price,   // snapshot at "purchase" time — see 1's note
                    });
                }
            }
        }

        // AddRange in dependency order isn't strictly required (EF Core's change tracker
        // sorts inserts by FK dependency automatically), but grouping the calls this way
        // keeps the method readable as "categories, then things that reference them."
        db.AddRange(categories);
        db.AddRange(brands);
        db.AddRange(tags);
        db.AddRange(customers);
        db.AddRange(products);
        db.AddRange(reviews);
        db.AddRange(orders);
        db.AddRange(orderItems);

        await db.SaveChangesAsync(ct);
    }
}
