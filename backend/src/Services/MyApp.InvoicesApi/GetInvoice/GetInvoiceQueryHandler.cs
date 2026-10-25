using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MyApp.Auth.Jwt;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.GetInvoice;

internal sealed class GetInvoiceQueryHandler(
    InvoicesDbContext db,
    IAuthorizationService authz)
    : IRequestHandler<GetInvoiceQuery, ErrorOr<InvoiceDto>>
{
    public async ValueTask<ErrorOr<InvoiceDto>> Handle(
        GetInvoiceQuery query,
        CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices.AsNoTracking()
            .SingleOrDefaultAsync(i => i.Id == query.Id, cancellationToken);

        if (invoice is null)
            return Error.NotFound("INVOICE.NOT_FOUND", $"Invoice '{query.Id}' was not found.");

        var check = await authz.AuthorizeAsync(
            query.User,
            invoice,
            new ResourcePermissionRequirement(Permissions.Invoices.View, requireOwnership: false));

        if (!check.Succeeded)
            return Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to view this invoice.");

        return new InvoiceDto(
            invoice.Id,
            invoice.OwnerId,
            invoice.Number,
            invoice.Amount,
            invoice.CreatedAt);
    }
}
