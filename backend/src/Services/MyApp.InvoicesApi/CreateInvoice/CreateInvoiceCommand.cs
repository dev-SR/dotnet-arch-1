using Shared.Application.Abstractions.Commands;

namespace MyApp.InvoicesApi.CreateInvoice;

/// <summary>Creates an invoice. Persistence is committed by <c>TransactionBehavior</c>.</summary>
public sealed record CreateInvoiceCommand(
    string Number,
    decimal Amount,
    Guid OwnerId) : ICommand<Invoice>;
