// src/Services/MyApp.Api/Features/Products/GetProductDetails/GetProductDetailsQuery.cs

using Microsoft.EntityFrameworkCore;
using MyApp.Features.Products.Specifications;
using MyApp.Persistence;
using Shared.Common.Specifications;

public sealed record GetProductDetailsQuery(Guid ProductId) : IQuery<ProductDetailsDto>;

public sealed record ReviewDto(string CustomerName, int Rating, string? Comment, DateTime CreatedAt);
public sealed record ProductDetailsDto(
    Guid Id, string Name, string? Description, decimal Price, int StockQuantity,
    string CategoryName, string BrandName, IReadOnlyList<string> Tags, IReadOnlyList<ReviewDto> Reviews);

internal sealed class GetProductDetailsQueryHandler(AppDbContext dbContext)
    : IRequestHandler<GetProductDetailsQuery, ErrorOr<ProductDetailsDto>>
{
    public async ValueTask<ErrorOr<ProductDetailsDto>> Handle(GetProductDetailsQuery query, CancellationToken ct)
    {
        // The Include/ThenInclude/AsSplitQuery spec from 2.5 — this DOES need the full
        // entity graph, not a projection, because the response includes nested
        // collections (Tags, Reviews with their Customer) that Select would otherwise
        // have to reconstruct by hand with several separate joins.
        var product = await dbContext.Products
            .Apply(new ProductDetailsSpecification(query.ProductId))
            .FirstOrDefaultAsync(ct);

        if (product is null)
            return Error.NotFound("PRODUCT.NOT_FOUND", $"Product '{query.ProductId}' was not found.");

        return new ProductDetailsDto(
            product.Id, product.Name, product.Description, product.Price, product.StockQuantity,
            product.Category.Name, product.Brand.Name,
            product.Tags.Select(t => t.Name).ToList(),
            product.Reviews.Select(r => new ReviewDto(r.Customer.Name, r.Rating, r.Comment, r.CreatedAt)).ToList());
    }
}
