namespace MyApp.Modules.Search.SearchProducts;

public sealed class SearchProductsEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("/search/products", HandleAsync)
            .WithName("SearchProducts")
            .WithTags("Search")
            .AllowAnonymous();
    }

    private static async Task<IResult> HandleAsync(
        string? q,
        IMediator mediator,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await mediator.Send(new SearchProductsQuery(q), ct);
        return result.MatchOk(http);
    }
}
