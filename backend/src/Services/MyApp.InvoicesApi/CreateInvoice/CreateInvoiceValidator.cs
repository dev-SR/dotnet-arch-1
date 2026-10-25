using FluentValidation;

namespace MyApp.InvoicesApi.CreateInvoice;

public sealed class CreateInvoiceValidator : AbstractValidator<CreateInvoiceCommand>
{
    public CreateInvoiceValidator()
    {
        RuleFor(x => x.Number).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Amount).GreaterThan(0);
        RuleFor(x => x.OwnerId).NotEmpty();
    }
}
