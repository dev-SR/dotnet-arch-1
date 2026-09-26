using FluentValidation;

namespace MyApp.Features.Products.DeleteProduct;

public sealed class DeleteProductValidator : AbstractValidator<DeleteProductCommand>
{
    public DeleteProductValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
