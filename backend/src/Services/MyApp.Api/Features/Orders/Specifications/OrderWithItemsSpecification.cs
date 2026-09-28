// src/Services/MyApp.Api/Features/Orders/Specifications/OrderWithItemsSpecification.cs
using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Features.Orders.Specifications;

public sealed class OrderWithItemsSpecification : SpecificationBase<Order>
{
    public OrderWithItemsSpecification(Guid orderId)
    {
        Where(o => o.Id == orderId);

        Include(q => q
            .Include(o => o.Items)
            .ThenInclude(i => i.Product)
            .ThenInclude(p => p.Category));   // Order → Items → Product → Category
        Include(q => q.Include(o => o.Customer));

        // Only one collection (Items) at the top level here, so no cartesian-explosion
        // risk — AsSplitQuery isn't needed. Leave the default (false) unless you add a
        // second sibling collection later.
    }
}
