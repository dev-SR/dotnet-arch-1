namespace MyApp.Modules.Catalog.Products.DeleteProduct;

public sealed record DeleteProductCommand(Guid Id) : ICommand;
