using Bogus;
using Microsoft.EntityFrameworkCore;
using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Products;
using MyApp.Modules.Catalog.Reviews;
using MyApp.Modules.Catalog.Tags;

namespace MyApp.Modules.Catalog.Persistence.Seeding;

public static class CatalogDataSeeder
{
    public static async Task SeedAsync(CatalogDbContext db, CancellationToken ct = default)
    {
        if (await db.Products.AnyAsync(ct))
            return;

        var random = new Random(20260927);
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

        // CustomerIds are opaque GUIDs (Users module owns Customer rows — no cross-DB FK).
        var customerIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();

        var products = new Faker<Product>()
            .RuleFor(p => p.Id, f => f.Random.Guid())
            .RuleFor(p => p.Name, f => f.Commerce.ProductName())
            .RuleFor(p => p.Description, f => f.Commerce.ProductDescription())
            .RuleFor(p => p.Price, f => f.Random.Decimal(5, 2000))
            .RuleFor(p => p.StockQuantity, f => f.Random.Int(0, 500))
            .RuleFor(p => p.IsActive, f => f.Random.Bool(0.9f))
            .RuleFor(p => p.IsDeleted, f => f.Random.Bool(0.03f))
            .RuleFor(p => p.CreatedAt, f => f.Date.Past(2))
            .RuleFor(p => p.CategoryId, f => f.PickRandom(categories).Id)
            .RuleFor(p => p.BrandId, f => f.PickRandom(brands).Id)
            .Generate(600);

        foreach (var p in products)
            p.Tags = tags.OrderBy(_ => random.Next()).Take(random.Next(1, 5)).ToList();

        var reviews = new List<Review>();
        var reviewFaker = new Faker<Review>()
            .RuleFor(r => r.Id, f => f.Random.Guid())
            .RuleFor(r => r.Rating, f => f.Random.Int(1, 5))
            .RuleFor(r => r.Comment, f => f.Random.Bool(0.7f) ? f.Rant.Review() : null)
            .RuleFor(r => r.CreatedAt, f => f.Date.Past(1));

        foreach (var product in products)
        {
            var reviewCount = random.Next(0, 7);
            for (var i = 0; i < reviewCount; i++)
            {
                var review = reviewFaker.Generate();
                review.ProductId = product.Id;
                review.CustomerId = customerIds[random.Next(customerIds.Count)];
                reviews.Add(review);
            }
        }

        db.AddRange(categories);
        db.AddRange(brands);
        db.AddRange(tags);
        db.AddRange(products);
        db.AddRange(reviews);
        await db.SaveChangesAsync(ct);
    }
}
