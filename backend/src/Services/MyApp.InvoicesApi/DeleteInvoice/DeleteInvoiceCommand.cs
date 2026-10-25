using System.Security.Claims;
using Shared.Application.Abstractions.Commands;
using Shared.Common.Errors;

namespace MyApp.InvoicesApi.DeleteInvoice;

public sealed record DeleteInvoiceCommand(Guid Id, ClaimsPrincipal User) : ICommand;
