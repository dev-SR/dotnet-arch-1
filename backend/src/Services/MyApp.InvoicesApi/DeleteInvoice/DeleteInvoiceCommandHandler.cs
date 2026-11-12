using Mediator;
using Microsoft.EntityFrameworkCore;
using MyApp.Auth.Jwt;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.DeleteInvoice;

internal sealed class DeleteInvoiceCommandHandler(InvoicesDbContext db)
    : IRequestHandler<DeleteInvoiceCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(
        DeleteInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .SingleOrDefaultAsync(i => i.Id == command.Id, cancellationToken);

        if (invoice is null)
            return Error.NotFound("INVOICE.NOT_FOUND", $"Invoice '{command.Id}' was not found.");

        if (!ResourceAccess.IsAllowed(command.Caller, invoice.OwnerId, Permissions.Invoices.Delete))
            return Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to delete this invoice.");

        db.Invoices.Remove(invoice);
        return default(Success);
    }
}
