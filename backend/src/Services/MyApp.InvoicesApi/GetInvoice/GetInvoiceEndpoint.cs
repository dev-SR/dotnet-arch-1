using System.Security.Claims;
using Mediator;
using Shared.Common.Errors.Http;

namespace MyApp.InvoicesApi.GetInvoice;

public sealed class GetInvoiceEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices/{id:guid}", HandleAsync)
            .WithName("GetInvoice")
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
        var result = await mediator.Send(new GetInvoiceQuery(id, user), ct);
        return result.MatchOk(http);
    }
}
