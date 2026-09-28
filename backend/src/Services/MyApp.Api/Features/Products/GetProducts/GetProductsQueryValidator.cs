using FluentValidation;
using Shared.Common.Pagination;

namespace MyApp.Features.Products.GetProducts;

public sealed class GetProductsQueryValidator : AbstractValidator<GetProductsQuery>
{
    // Allow-list against the DTO the client actually receives: sorting the raw entity would
    // let a caller order by a column that's never exposed in the response at all.
    private static readonly string[] Sortable =
    [nameof(ProductListItemDto.Name), nameof(ProductListItemDto.Price),
        nameof(ProductListItemDto.StockQuantity), nameof(ProductListItemDto.CreatedAt)];

    public GetProductsQueryValidator()
    {
        RuleFor(x => x.Filter.Page).GreaterThanOrEqualTo(1).OverridePropertyName("page");
        RuleFor(x => x.Filter.PageSize)
            .InclusiveBetween(1, PagedQueryExtensions.MaxPageSize).OverridePropertyName("pageSize");
        RuleFor(x => x.Filter.OrderBy)
            .Must(o => Sortable.Contains(o, StringComparer.OrdinalIgnoreCase))
            .When(x => !string.IsNullOrWhiteSpace(x.Filter.OrderBy))
            .WithMessage($"orderBy must be one of: {string.Join(", ", Sortable)}.")
            .OverridePropertyName("orderBy");
        RuleFor(x => x.Filter.Search).MaximumLength(100).OverridePropertyName("search");

        RuleFor(x => x.Filter.MinPrice).GreaterThanOrEqualTo(0)
            .When(x => x.Filter.MinPrice.HasValue).OverridePropertyName("minPrice");
        RuleFor(x => x.Filter.MaxPrice).GreaterThan(0)
            .When(x => x.Filter.MaxPrice.HasValue).OverridePropertyName("maxPrice");

        // Cross-field rule: FluentValidation isn't limited to one property per rule.
        RuleFor(x => x.Filter)
            .Must(f => !f.MinPrice.HasValue || !f.MaxPrice.HasValue || f.MinPrice <= f.MaxPrice)
            .WithMessage("minPrice must not be greater than maxPrice.")
            .OverridePropertyName("minPrice");
    }
}
