using MyApp.Auth.Jwt;
using Shared.Application.Abstractions.Queries;

namespace MyApp.InvoicesApi.GetInvoice;

public sealed record InvoiceDto(
    Guid Id,
    Guid OwnerId,
    string Number,
    decimal Amount,
    DateTime CreatedAt);

public sealed record GetInvoiceQuery(Guid Id, Caller Caller) : IQuery<InvoiceDto>;
