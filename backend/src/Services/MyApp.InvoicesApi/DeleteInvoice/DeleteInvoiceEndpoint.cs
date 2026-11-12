using System.Security.Claims;
using Mediator;
using MyApp.Auth.Jwt;
using Shared.Common.Errors.Http;

namespace MyApp.InvoicesApi.DeleteInvoice;

public sealed class DeleteInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapDelete("/invoices/{id:guid}", HandleAsync)
            .WithName("DeleteInvoice")
            .WithTags("Invoices")
            .RequireAuthorization();
    }

    private static async Task<IResult> HandleAsync(
        Guid id,
        ClaimsPrincipal user,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new DeleteInvoiceCommand(id, Caller.From(user)), ct);
        return result.MatchNoContent(http);
    }
}
