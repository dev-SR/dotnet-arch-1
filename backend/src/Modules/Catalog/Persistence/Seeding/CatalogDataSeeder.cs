using Bogus;
using Microsoft.EntityFrameworkCore;
using MyApp.Modular;
using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Products;
using MyApp.Modules.Catalog.Reviews;
using MyApp.Modules.Catalog.Tags;

namespace MyApp.Modules.Catalog.Persistence.Seeding;

public sealed class CatalogDataSeeder(CatalogDbContext db) : IDataSeeder
{
    public async Task SeedAsync(CancellationToken ct = default)
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

        var customerIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToList();

        var products = new List<Product>(600);
        var faker = new Faker();
        for (var i = 0; i < 600; i++)
        {
            var name = faker.Commerce.ProductName();
            if (name.Length < 3)
                name = name.PadRight(3, 'x');

            var created = Product.Create(
                name,
                faker.Random.Decimal(5, 2000),
                faker.Random.Int(0, 500),
                faker.PickRandom(categories),
                faker.PickRandom(brands));

            if (created.IsError)
                continue;

            var product = created.Value;
            product.ClearDomainEvents();

            var seedTags = tags.OrderBy(_ => random.Next()).Take(random.Next(1, 5)).ToList();
            var isActive = faker.Random.Bool(0.9f);
            product.ConfigureForSeed(
                faker.Commerce.ProductDescription(),
                faker.Date.Past(2),
                isActive,
                seedTags);

            if (faker.Random.Bool(0.03f))
                _ = product.MarkDeleted();

            products.Add(product);
        }

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
