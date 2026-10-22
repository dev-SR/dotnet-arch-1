using MyApp.Modules.Catalog.Brands;
using MyApp.Modules.Catalog.Categories;
using MyApp.Modules.Catalog.Reviews;
using MyApp.Modules.Catalog.Tags;
using Shared.Common.Errors;
using Shared.Domain;

namespace MyApp.Modules.Catalog.Products;

public sealed class Product : Aggregate
{
    // EF + Bogus need a parameterless ctor; mutations go through domain methods.
    private Product() { }

    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal Price { get; private set; }

    public int Stock { get; private set; }
    public int StockQuantity { get; private set; }
    public bool IsActive { get; private set; } = true;
    public bool IsDeleted { get; private set; }

    public Guid CategoryId { get; private set; }
    public Category Category { get; private set; } = null!;

    public Guid BrandId { get; private set; }
    public Brand Brand { get; private set; } = null!;

    public ICollection<Tag> Tags { get; private set; } = [];
    public ICollection<Review> Reviews { get; private set; } = [];

    /// <summary>Factory with domain invariants. Raises <see cref="ProductCreatedDomainEvent"/>.</summary>
    public static ErrorOr<Product> Create(
        string name,
        decimal price,
        int stock,
        Category category,
        Brand brand)
    {
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(brand);

        var errors = ValidateDetails(name, price, stock);
        if (errors.Count > 0)
            return errors;

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Price = price,
            Stock = stock,
            StockQuantity = stock,
            Category = category,
            CategoryId = category.Id,
            Brand = brand,
            BrandId = brand.Id,
            IsActive = true,
            IsDeleted = false,
        };
        product.AddDomainEvent(new ProductCreatedDomainEvent(product.Id, product.Name));
        return product;
    }

    public ErrorOr<Success> UpdateDetails(string name, decimal price, int stock, string categoryName)
    {
        if (IsDeleted)
            return Error.Conflict("PRODUCT.DELETED", "Cannot update a deleted product.");

        var errors = ValidateDetails(name, price, stock);
        if (string.IsNullOrWhiteSpace(categoryName) || categoryName.Trim().Length is < 3 or > 50)
            errors.Add(Error.Validation("PRODUCT.CATEGORY", "Category must be between 3 and 50 characters."));

        if (errors.Count > 0)
            return errors;

        Name = name.Trim();
        Price = price;
        Stock = stock;
        StockQuantity = stock;
        Category.Name = categoryName.Trim();
        return default(Success);
    }

    public ErrorOr<Success> MarkDeleted()
    {
        if (IsDeleted)
            return Error.Conflict("PRODUCT.DELETED", "Product is already deleted.");

        IsDeleted = true;
        IsActive = false;
        return default(Success);
    }

    /// <summary>Dev/test seed only — skips domain events (caller should <see cref="ClearDomainEvents"/>).</summary>
    internal void ConfigureForSeed(
        string? description,
        DateTime createdAt,
        bool isActive,
        IReadOnlyCollection<Tag> tags)
    {
        Description = description;
        CreatedAt = createdAt;
        IsActive = isActive;
        Tags = tags.ToList();
    }

    private static List<Error> ValidateDetails(string name, decimal price, int stock)
    {
        var errors = new List<Error>();
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is < 3 or > 100)
            errors.Add(Error.Validation("PRODUCT.NAME", "Name must be between 3 and 100 characters."));
        if (price <= 0)
            errors.Add(Error.Validation("PRODUCT.PRICE", "Price must be greater than zero."));
        if (stock < 0)
            errors.Add(Error.Validation("PRODUCT.STOCK", "Stock cannot be negative."));
        return errors;
    }
}
