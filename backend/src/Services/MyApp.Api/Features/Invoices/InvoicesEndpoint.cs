using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using MyApp.Features.Auth;
using MyApp.Features.Auth.Authorization;
using MyApp.Persistence;

namespace MyApp.Features.Invoices;

public sealed class InvoicesEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/invoices")
            .WithTags("Invoices")
            .RequireAuthorization();

        group.MapPost("/", CreateAsync).WithName("CreateInvoice");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetInvoice");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateInvoice");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteInvoice");
    }

    private static async Task<IResult> CreateAsync(
        CreateInvoiceBody body,
        ClaimsPrincipal user,
        AppDbContext db,
        CancellationToken ct)
    {
        // Creating an invoice only needs auth; ownership = caller.
        var invoice = new Invoice
        {
            OwnerId = user.GetUserId(),
            Number = body.Number.Trim(),
            Amount = body.Amount,
        };
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/invoices/{invoice.Id}", invoice);
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        ClaimsPrincipal user,
        IAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
            return Results.NotFound();

        var check = await authz.AuthorizeAsync(
            user,
            invoice,
            new ResourcePermissionRequirement(Permissions.Invoices.View, requireOwnership: false));

        return check.Succeeded ? Results.Ok(invoice) : Results.Forbid();
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateInvoiceBody body,
        ClaimsPrincipal user,
        IAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
            return Results.NotFound();

        var check = await authz.AuthorizeAsync(
            user,
            invoice,
            new ResourcePermissionRequirement(Permissions.Invoices.Edit));

        if (!check.Succeeded)
            return Results.Forbid();

        invoice.Number = body.Number.Trim();
        invoice.Amount = body.Amount;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        ClaimsPrincipal user,
        IAuthorizationService authz,
        AppDbContext db,
        CancellationToken ct)
    {
        var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id, ct);
        if (invoice is null)
            return Results.NotFound();

        var check = await authz.AuthorizeAsync(
            user,
            invoice,
            new ResourcePermissionRequirement(Permissions.Invoices.Delete));

        if (!check.Succeeded)
            return Results.Forbid();

        db.Invoices.Remove(invoice);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public sealed record CreateInvoiceBody(string Number, decimal Amount);
    public sealed record UpdateInvoiceBody(string Number, decimal Amount);
}
