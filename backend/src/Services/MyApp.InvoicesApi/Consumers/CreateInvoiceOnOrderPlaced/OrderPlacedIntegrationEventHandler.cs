using Microsoft.EntityFrameworkCore;
using MyApp.Messaging.IntegrationEvents;
using Shared.Application.Persistence;

namespace MyApp.InvoicesApi.Consumers.CreateInvoiceOnOrderPlaced;

public sealed class OrderPlacedIntegrationEventHandler
{
    public async Task Handle(
        OrderPlacedIntegrationEvent message,
        InvoicesDbContext db,
        CancellationToken ct)
    {
        var number = $"ORD-{message.OrderId:N}"[..20];
        var exists = await db.Invoices.AnyAsync(i => i.Number == number, ct);
        if (exists)
            return;

        db.Invoices.Add(new Invoice
        {
            OwnerId = message.CustomerId,
            Number = number,
            Amount = message.Total,
        });

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DbErrors.IsUniqueViolation(ex))
        {
            db.ChangeTracker.Clear();
        }
    }
}
