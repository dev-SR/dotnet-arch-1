namespace MyApp.Features.Products.DeleteProduct;

public sealed record DeleteProductCommand(Guid Id) : ICommand;
