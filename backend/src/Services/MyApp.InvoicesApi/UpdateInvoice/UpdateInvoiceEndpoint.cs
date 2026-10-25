using System.Security.Claims;
using Mediator;
using Shared.Common.Errors.Http;

namespace MyApp.InvoicesApi.UpdateInvoice;

public sealed record UpdateInvoiceRequest(string Number, decimal Amount);

public sealed class UpdateInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPut("/invoices/{id:guid}", HandleAsync)
            .WithName("UpdateInvoice")
            .WithTags("Invoices")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        UpdateInvoiceRequest body,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new UpdateInvoiceCommand(id, body.Number, body.Amount, user),
            ct);
        return result.MatchNoContent(http);
    }
}
