using MyApp.Features.Orders;
using MyApp.Features.Reviews;

namespace MyApp.Features.Customers;

public sealed class Customer
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public ICollection<Review> Reviews { get; init; } = [];
    public ICollection<Order> Orders { get; init; } = [];
}
