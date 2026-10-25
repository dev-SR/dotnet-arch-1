using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MyApp.Auth.Jwt;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.DeleteInvoice;

internal sealed class DeleteInvoiceCommandHandler(
    InvoicesDbContext db,
    IAuthorizationService authz)
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

        var check = await authz.AuthorizeAsync(
            command.User,
            invoice,
            new ResourcePermissionRequirement(Permissions.Invoices.Delete));

        if (!check.Succeeded)
            return Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to delete this invoice.");

        db.Invoices.Remove(invoice);
        return default(Success);
    }
}
