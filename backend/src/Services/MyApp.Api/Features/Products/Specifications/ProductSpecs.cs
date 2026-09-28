// src/Services/MyApp.Api/Features/Products/Specifications/ProductSpecs.cs

using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Features.Products.Specifications;

public sealed class ActiveProductSpec : SpecificationBase<Product>
{
    public ActiveProductSpec()
    {
        Where(p => p.IsActive && !p.IsDeleted);
    }
    public static readonly ISpecification<Product> Instance = new ActiveProductSpec();
}

public sealed class InStockSpecification : SpecificationBase<Product>
{
    public InStockSpecification(int minQuantity = 1)
    {
        Where(p => p.StockQuantity >= minQuantity);
    }
}

public sealed class PriceRangeSpecification : SpecificationBase<Product>
{
    public PriceRangeSpecification(decimal? min, decimal? max)
    {
        if (min is { } lo)
            Where(p => p.Price >= lo);
        if (max is { } hi)
            Where(p => p.Price <= hi);
    }
}

public sealed class ByCategorySpecification : SpecificationBase<Product>
{
    public ByCategorySpecification(Guid categoryId) => Where(p => p.CategoryId == categoryId);
}

public sealed class ByBrandSpecification : SpecificationBase<Product>
{
    public ByBrandSpecification(Guid brandId) => Where(p => p.BrandId == brandId);
}

public sealed class ByTagSpecification : SpecificationBase<Product>
{
    // Tags.Any translates to an EXISTS against the ProductTag join table — normal EF SQL,
    // not an in-memory filter.
    public ByTagSpecification(Guid tagId) => Where(p => p.Tags.Any(t => t.Id == tagId));
}

public sealed class ProductSearchSpecification : SpecificationBase<Product>
{
    // MyApp uses SQLite — EF.Functions.Like (core EF). ILike is Npgsql/Postgres-only.
    public ProductSearchSpecification(string term)
    {
        var pattern = $"%{term}%";
        Where(p =>
            EF.Functions.Like(p.Name, pattern) ||
            (p.Description != null && EF.Functions.Like(p.Description, pattern)));
    }
}
