using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.InvoicesApi.CreateInvoice;

public sealed record CreateInvoiceRequest(string Number, decimal Amount);

public sealed class CreateInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices", HandleAsync)
            .WithName("CreateInvoice")
            .WithTags("Invoices")
            .RequireAuthorization(Permissions.Invoices.Edit);
    }

    private static async Task<IResult> HandleAsync(
        CreateInvoiceRequest body,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateInvoiceCommand(body.Number, body.Amount, user.GetUserId()),
            ct);
        return result.MatchCreated(http, inv => $"/invoices/{inv.Id}");
    }
}
