// src/Services/MyApp.Api/Features/Products/Specifications/ProductDetailsSpecification.cs

using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Modules.Catalog.Products.Specifications;

public sealed class ProductDetailsSpecification : SpecificationBase<Product>
{
    public ProductDetailsSpecification(Guid productId)
    {
        Where(p => p.Id == productId);

        Include(q => q.Include(p => p.Category));
        Include(q => q.Include(p => p.Brand));
        Include(q => q.Include(p => p.Tags));

        // Customer lives in Users module — Reviews only expose CustomerId across the boundary.
        Include(q => q.Include(p => p.Reviews));

        // Reviews AND Tags are BOTH collections on the same root — use AsSplitQuery.
        AsSplitQuery = true;
    }
}
