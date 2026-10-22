using Shared.Domain;

namespace MyApp.Modules.Catalog.Products;

public sealed record ProductCreatedDomainEvent(Guid ProductId, string Name) : DomainEvent;
