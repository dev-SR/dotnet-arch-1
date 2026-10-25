using System.Security.Claims;
using Shared.Application.Abstractions.Commands;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.UpdateInvoice;

public sealed record UpdateInvoiceCommand(
    Guid Id,
    string Number,
    decimal Amount,
    ClaimsPrincipal User) : ICommand;
