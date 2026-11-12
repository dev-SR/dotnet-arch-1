using MyApp.Auth.Jwt;
using Shared.Application.Abstractions.Commands;

namespace MyApp.InvoicesApi.UpdateInvoice;

public sealed record UpdateInvoiceCommand(
    Guid Id,
    string Number,
    decimal Amount,
    Caller Caller) : ICommand;
