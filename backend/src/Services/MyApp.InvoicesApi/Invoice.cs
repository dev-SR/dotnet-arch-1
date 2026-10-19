using MyApp.Auth.Jwt;

namespace MyApp.InvoicesApi;

public sealed class Invoice : IOwnedResource
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public required string Number { get; set; }
    public decimal Amount { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}
