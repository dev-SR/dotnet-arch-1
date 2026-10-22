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
        var errors = Validate(command.Number, command.Amount);
        if (errors is not null)
            return ValueTask.FromResult(ErrorOr<Invoice>.From(errors));

        var invoice = new Invoice
        {
            OwnerId = command.OwnerId,
            Number = command.Number.Trim(),
            Amount = command.Amount,
        };
        db.Invoices.Add(invoice);

        // SaveChanges via TransactionBehavior
        return ValueTask.FromResult<ErrorOr<Invoice>>(invoice);
    }

    private static List<Error>? Validate(string number, decimal amount)
    {
        var errors = new List<Error>();
        if (string.IsNullOrWhiteSpace(number))
            errors.Add(Error.ValidationField("number", "Number is required."));
        else if (number.Trim().Length > 64)
            errors.Add(Error.ValidationField("number", "Number must be at most 64 characters."));

        if (amount <= 0)
            errors.Add(Error.ValidationField("amount", "Amount must be greater than zero."));

        return errors.Count == 0 ? null : errors;
    }
}
