using Mediator;
using MyApp.InvoicesApi.GetInvoice;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.CreateInvoice;

internal sealed class CreateInvoiceCommandHandler(InvoicesDbContext db)
    : IRequestHandler<CreateInvoiceCommand, ErrorOr<InvoiceDto>>
{
    public ValueTask<ErrorOr<InvoiceDto>> Handle(
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
        return ValueTask.FromResult<ErrorOr<InvoiceDto>>(new InvoiceDto(
            invoice.Id,
            invoice.OwnerId,
            invoice.Number,
            invoice.Amount,
            invoice.CreatedAt));
    }
}
