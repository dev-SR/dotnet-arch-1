using Microsoft.AspNetCore.Mvc;
using Shared.Common.Pagination;

namespace MyApp.Features.Products.GetProducts;

public sealed class ProductFilter:PageRequest
{

    public Guid? CategoryId { get; init; }
    public Guid? BrandId { get; init; }
    public Guid? TagId { get; init; }
    public decimal? MinPrice { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool? InStockOnly { get; init; }
    // public string? Search { get; init; }  // pa
}
