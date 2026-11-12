using MyApp.Auth.Jwt;
using Shared.Application.Abstractions.Commands;

namespace MyApp.InvoicesApi.DeleteInvoice;

public sealed record DeleteInvoiceCommand(Guid Id, Caller Caller) : ICommand;
