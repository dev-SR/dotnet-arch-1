using FluentValidation;

namespace MyApp.Features.Products.CreateProduct;

public sealed class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name).MinimumLength(3).MaximumLength(100);
        RuleFor(x => x.Category).MinimumLength(3).MaximumLength(50);
        RuleFor(x => x.Price).GreaterThan(0);
        RuleFor(x => x.Stock).GreaterThanOrEqualTo(0);
    }
}
