using FluentValidation;

namespace MyApp.InvoicesApi.UpdateInvoice;

public sealed class UpdateInvoiceValidator : AbstractValidator<UpdateInvoiceCommand>
{
    public UpdateInvoiceValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Number).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Amount).GreaterThan(0);
    }
}
