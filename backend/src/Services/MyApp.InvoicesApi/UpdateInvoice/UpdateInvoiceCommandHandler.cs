using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MyApp.Auth.Jwt;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.UpdateInvoice;

internal sealed class UpdateInvoiceCommandHandler(
    InvoicesDbContext db,
    IAuthorizationService authz)
    : IRequestHandler<UpdateInvoiceCommand, ErrorOr<Success>>
{
    public async ValueTask<ErrorOr<Success>> Handle(
        UpdateInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .SingleOrDefaultAsync(i => i.Id == command.Id, cancellationToken);

        if (invoice is null)
            return Error.NotFound("INVOICE.NOT_FOUND", $"Invoice '{command.Id}' was not found.");

        var check = await authz.AuthorizeAsync(
            command.User,
            invoice,
            new ResourcePermissionRequirement(Permissions.Invoices.Edit));

        if (!check.Succeeded)
            return Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to edit this invoice.");

        var number = command.Number.Trim();
        var taken = await db.Invoices.AnyAsync(
            i => i.Number == number && i.Id != command.Id,
            cancellationToken);
        if (taken)
            return Error.Conflict("INVOICE.NUMBER_EXISTS", $"Invoice number '{number}' already exists.");

        invoice.Number = number;
        invoice.Amount = command.Amount;
        return default(Success);
    }
}
