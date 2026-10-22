using System.Security.Claims;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using MyApp.Auth.Jwt;
using MyApp.InvoicesApi;
using MyApp.InvoicesApi.CreateInvoice;
using Shared.Application.Behaviors;
using Shared.Application.Persistence;
using Shared.Common.Errors;
using Shared.Common.Errors.Http;
using Shared.Common.Exceptions.Http;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSharedExceptionHandling();

// Same shared secret as AuthApi — claim-only authz (no IAM DB on this host).
builder.Services.AddMyAppAuth(builder.Configuration);
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddTransactionalDbContext<InvoicesDbContext>((_, o) =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Invoices")
                ?? "Data Source=invoices.db"));

builder.Services.RegisterRequestHandlers(typeof(AssemblyMarker).Assembly);
builder.Services.AddPipelineBehavior(typeof(TransactionBehavior<,>));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info = new OpenApiInfo
        {
            Title = "Invoices API",
            Version = "v1",
            Description = "Invoice CRUD behind the gateway. JWT from AuthApi (permission + ownership).",
        };

        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "Paste access_token from AuthApi POST /auth/login (via gateway).",
        };

        return Task.CompletedTask;
    });

    options.AddOperationTransformer((operation, context, _) =>
    {
        var allowAnonymous = context.Description.ActionDescriptor.EndpointMetadata
            .OfType<IAllowAnonymous>().Any();
        if (!allowAnonymous)
        {
            operation.Security =
            [
                new OpenApiSecurityRequirement
                {
                    [new OpenApiSecuritySchemeReference("Bearer")] = [],
                },
            ];
        }

        return Task.CompletedTask;
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<InvoicesDbContext>();
    db.Database.EnsureCreated();
}

app.UseSharedExceptionHandling();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi().AllowAnonymous();
app.MapGet("/health", () => Results.Ok(new { status = "ok", service = "invoices" }))
    .AllowAnonymous()
    .ExcludeFromDescription();

var group = app.MapGroup("/invoices")
    .RequireAuthorization()
    .WithTags("Invoices");

// Create needs Edit claim (no separate Create permission in the IAM seed).
// Demonstrates microservice use of Shared.Application TransactionBehavior (no MyApp.Modular).
group.MapPost("/", async (
    CreateInvoiceBody body,
    ClaimsPrincipal user,
    IMediator mediator,
    HttpContext http,
    CancellationToken ct) =>
{
    var result = await mediator.Send(
        new CreateInvoiceCommand(body.Number, body.Amount, user.GetUserId()),
        ct);
    return result.MatchCreated(http, inv => $"/invoices/{inv.Id}");
}).RequireAuthorization(Permissions.Invoices.Edit);

group.MapGet("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal user,
    IAuthorizationService authz,
    InvoicesDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var invoice = await db.Invoices.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id, ct);
    if (invoice is null)
        return Error.NotFound("INVOICE.NOT_FOUND", $"Invoice '{id}' was not found.").ToProblem(http);

    var check = await authz.AuthorizeAsync(
        user,
        invoice,
        new ResourcePermissionRequirement(Permissions.Invoices.View, requireOwnership: false));

    return check.Succeeded
        ? Results.Ok(invoice)
        : Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to view this invoice.").ToProblem(http);
});

group.MapPut("/{id:guid}", async (
    Guid id,
    UpdateInvoiceBody body,
    ClaimsPrincipal user,
    IAuthorizationService authz,
    InvoicesDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var validation = ValidateInvoice(body.Number, body.Amount);
    if (validation is not null)
        return validation.ToProblem(http);

    var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id, ct);
    if (invoice is null)
        return Error.NotFound("INVOICE.NOT_FOUND", $"Invoice '{id}' was not found.").ToProblem(http);

    var check = await authz.AuthorizeAsync(
        user,
        invoice,
        new ResourcePermissionRequirement(Permissions.Invoices.Edit));

    if (!check.Succeeded)
        return Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to edit this invoice.").ToProblem(http);

    invoice.Number = body.Number.Trim();
    invoice.Amount = body.Amount;

    try
    {
        await db.SaveChangesAsync(ct);
    }
    catch (DbUpdateException)
    {
        return Error.Conflict(
                "INVOICE.NUMBER_EXISTS",
                $"Invoice number '{invoice.Number}' already exists.")
            .ToProblem(http);
    }

    return Results.NoContent();
});

group.MapDelete("/{id:guid}", async (
    Guid id,
    ClaimsPrincipal user,
    IAuthorizationService authz,
    InvoicesDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var invoice = await db.Invoices.SingleOrDefaultAsync(i => i.Id == id, ct);
    if (invoice is null)
        return Error.NotFound("INVOICE.NOT_FOUND", $"Invoice '{id}' was not found.").ToProblem(http);

    var check = await authz.AuthorizeAsync(
        user,
        invoice,
        new ResourcePermissionRequirement(Permissions.Invoices.Delete));

    if (!check.Succeeded)
        return Error.Forbidden("INVOICE.FORBIDDEN", "You are not allowed to delete this invoice.").ToProblem(http);

    db.Invoices.Remove(invoice);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

app.Run();

static List<Error>? ValidateInvoice(string number, decimal amount)
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

public sealed record CreateInvoiceBody(string Number, decimal Amount);
public sealed record UpdateInvoiceBody(string Number, decimal Amount);
