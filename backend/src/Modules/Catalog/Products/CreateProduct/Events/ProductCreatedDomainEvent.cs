using Shared.Domain;

namespace MyApp.Modules.Catalog.Products.CreateProduct.Events;

public sealed record ProductCreatedDomainEvent(Guid ProductId, string Name, decimal Price) : DomainEvent;
