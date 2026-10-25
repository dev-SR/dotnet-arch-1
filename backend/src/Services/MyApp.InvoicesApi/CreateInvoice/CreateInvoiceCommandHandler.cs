using Mediator;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.CreateInvoice;

internal sealed class CreateInvoiceCommandHandler(InvoicesDbContext db)
    : IRequestHandler<CreateInvoiceCommand, ErrorOr<Invoice>>
{
    public ValueTask<ErrorOr<Invoice>> Handle(
        CreateInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var invoice = new Invoice
        {
            OwnerId = command.OwnerId,
            Number = command.Number.Trim(),
            Amount = command.Amount,
        };
        db.Invoices.Add(invoice);
        return ValueTask.FromResult<ErrorOr<Invoice>>(invoice);
    }
}
