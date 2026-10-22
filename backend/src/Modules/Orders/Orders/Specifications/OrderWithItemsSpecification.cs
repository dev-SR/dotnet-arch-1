using Microsoft.EntityFrameworkCore;
using Shared.Common.Specifications;

namespace MyApp.Modules.Orders.Specifications;

public sealed class OrderWithItemsSpecification : SpecificationBase<Order>
{
    public OrderWithItemsSpecification(Guid orderId)
    {
        Where(o => o.Id == orderId);
        Include(q => q.Include(o => o.Items));
    }
}
