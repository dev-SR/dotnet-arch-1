namespace MyApp.Modules.Users.Customers;

public sealed class Customer
{
    public Guid Id { get; init; }
    public required string Name { get; set; }
    public required string Email { get; set; }
}
