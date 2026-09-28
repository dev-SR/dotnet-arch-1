// src/Services/MyApp.Api/Features/Products/Specifications/ProductDetailsSpecification.cs

using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Features.Products.Specifications;

public sealed class ProductDetailsSpecification : SpecificationBase<Product>
{
    public ProductDetailsSpecification(Guid productId)
    {
        Where(p => p.Id == productId);

        Include(q => q.Include(p => p.Category));
        Include(q => q.Include(p => p.Brand));
        Include(q => q.Include(p => p.Tags));

        // Two-level chain: load each Review's Customer too, not just the Review rows.
        Include(q => q.Include(p => p.Reviews).ThenInclude(r => r.Customer));

        // Reviews AND Tags are BOTH collections loaded on the same root Product. Without
        // AsSplitQuery, EF Core joins everything into one SQL query, and the result is
        // effectively #Reviews × #Tags rows for a single product's data — a real cartesian
        // explosion that gets worse combinatorially as either collection grows. AsSplitQuery
        // instead issues one SQL query per collection (still 1 root query + 1 per included
        // collection, not per row) and stitches them together in memory. Use it whenever a
        // spec includes more than one *collection* navigation at the same level.
        AsSplitQuery = true;
    }
}
