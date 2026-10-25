using Shared.Domain;

namespace MyApp.Modules.Catalog.Products.UpdateProduct.Events;

public sealed record ProductUpdatedDomainEvent(Guid ProductId, string Name, decimal Price) : DomainEvent;
